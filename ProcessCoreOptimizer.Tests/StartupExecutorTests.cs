using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProcessCoreOptimizer.Tests;

internal sealed class FakeStartupBackend : IStartupConfigurationBackend
{
    public ActualStartupConfiguration Actual;
    public List<(StartupOperation Op, DesiredStartupConfiguration Desired)> Executed = new();
    public Func<StartupOperation, DesiredStartupConfiguration, StartupOperationResult>? ExecuteBehavior;
    public int ReadDelayMs;

    public FakeStartupBackend(ActualStartupConfiguration initial) => Actual = initial;

    public Task<ActualStartupConfiguration> ReadActualAsync(DesiredStartupConfiguration desired, CancellationToken ct = default)
    {
        if (ReadDelayMs > 0) Task.Delay(ReadDelayMs, ct).Wait(ct);
        return Task.FromResult(Actual);
    }

    public Task<StartupOperationResult> ExecuteAsync(StartupOperation op, DesiredStartupConfiguration desired, CancellationToken ct = default)
    {
        Executed.Add((op, desired));
        if (ExecuteBehavior != null) return Task.FromResult(ExecuteBehavior(op, desired));
        Evolve(op, desired);
        return Task.FromResult(new StartupOperationResult(true));
    }

    private void Evolve(StartupOperation op, DesiredStartupConfiguration desired)
    {
        var exe = @"C:\PCO\ProcessCoreOptimizer.exe";
        switch (op)
        {
            case StartupOperation.CreateOrUpdateRegistry:
                Actual = Actual with
                {
                    Registry = new RegistryStartupSnapshot(true, exe, desired.Arguments, true, true, true)
                };
                break;
            case StartupOperation.RemoveRegistry:
                Actual = Actual with
                {
                    Registry = new RegistryStartupSnapshot(false, null, string.Empty, false, false, false)
                };
                break;
            case StartupOperation.CreateOrUpdateScheduledTask:
                Actual = Actual with
                {
                    Task = new ScheduledTaskStartupSnapshot(true, true, exe, desired.Arguments, true, true, true, true, true)
                };
                break;
            case StartupOperation.RemoveScheduledTask:
                Actual = Actual with
                {
                    Task = new ScheduledTaskStartupSnapshot(false, false, null, string.Empty, false, false, false, false, false)
                };
                break;
        }
    }
}

[TestClass]
public sealed class StartupExecutorTests
{
    private static ActualStartupConfiguration Empty() => new(
        new RegistryStartupSnapshot(false, null, string.Empty, false, false, false),
        new ScheduledTaskStartupSnapshot(false, false, null, string.Empty, false, false, false, false, false));

    [TestMethod]
    public async Task DisabledToNormal_EndToEnd_RegistryHealthy()
    {
        var backend = new FakeStartupBackend(Empty());
        var result = await new StartupConfigurationExecutor(backend).ApplyAsync(new DesiredStartupConfiguration(true, StartupWindowMode.Normal, false));
        Assert.IsTrue(result.Success);
        Assert.AreEqual(StartupState.Registry, result.FinalEvaluation.State);
        CollectionAssert.AreEquivalent(new[] { StartupOperation.CreateOrUpdateRegistry }, result.OperationsAttempted.ToList());
    }

    [TestMethod]
    public async Task NormalToElevated_CreateBeforeRemove()
    {
        var exe = @"C:\PCO\ProcessCoreOptimizer.exe";
        var start = new ActualStartupConfiguration(
            new RegistryStartupSnapshot(true, exe, "", true, true, true),
            new ScheduledTaskStartupSnapshot(false, false, null, string.Empty, false, false, false, false, false));
        var backend = new FakeStartupBackend(start);
        var result = await new StartupConfigurationExecutor(backend).ApplyAsync(new DesiredStartupConfiguration(true, StartupWindowMode.Tray, true));
        Assert.IsTrue(result.Success);
        Assert.AreEqual(StartupState.ElevatedTask, result.FinalEvaluation.State);
        var ops = backend.Executed.Select(e => e.Op).ToList();
        Assert.IsTrue(ops.IndexOf(StartupOperation.CreateOrUpdateScheduledTask) < ops.IndexOf(StartupOperation.RemoveRegistry),
            "Target channel first, stale channel after — never delete before the replacement exists.");
    }

    [TestMethod]
    public async Task FailedCreate_NeverRemovesExisting()
    {
        var exe = @"C:\PCO\ProcessCoreOptimizer.exe";
        var start = new ActualStartupConfiguration(
            new RegistryStartupSnapshot(true, exe, "", true, true, true),
            new ScheduledTaskStartupSnapshot(false, false, null, string.Empty, false, false, false, false, false));
        var backend = new FakeStartupBackend(start)
        {
            ExecuteBehavior = (op, d) => new StartupOperationResult(false, Error: "boom")
        };
        var result = await new StartupConfigurationExecutor(backend).ApplyAsync(new DesiredStartupConfiguration(true, StartupWindowMode.Tray, true));
        Assert.IsFalse(result.Success);
        Assert.AreEqual(1, backend.Executed.Count, "After a failed create, the stale entry must be left alone.");
        Assert.AreEqual(StartupOperation.CreateOrUpdateScheduledTask, backend.Executed[0].Op);
    }

    [TestMethod]
    public async Task UacCancelled_SurfacesCleanly()
    {
        var backend = new FakeStartupBackend(Empty())
        {
            ExecuteBehavior = (op, d) => op == StartupOperation.CreateOrUpdateScheduledTask
                ? new StartupOperationResult(false, ElevationRequired: true, ElevationCancelled: true, Error: "cancelled")
                : new StartupOperationResult(true)
        };
        var result = await new StartupConfigurationExecutor(backend).ApplyAsync(new DesiredStartupConfiguration(true, StartupWindowMode.Normal, true));
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.WasElevationRequired);
        Assert.IsTrue(result.WasElevationCancelled);
    }

    [TestMethod]
    public async Task Coordinator_LatestWins()
    {
        var backend = new FakeStartupBackend(Empty()) { ReadDelayMs = 30 };
        var coordinator = new StartupApplyCoordinator(new StartupConfigurationExecutor(backend));
        var off = new DesiredStartupConfiguration(false, StartupWindowMode.Normal, false);
        var on = new DesiredStartupConfiguration(true, StartupWindowMode.Minimized, false);
        var first = coordinator.ApplyLatestAsync(off);
        var second = coordinator.ApplyLatestAsync(on);
        var results = await Task.WhenAll(first, second);
        Assert.IsTrue(results[1].Success);
        Assert.AreEqual(StartupState.Registry, results[1].FinalEvaluation.State);
        Assert.IsTrue(backend.Executed.Any(e => e.Desired.WindowMode == StartupWindowMode.Minimized),
            "The newest desired configuration must be applied.");
    }

    [TestMethod]
    public void HelperCommand_RoundTrip()
    {
        var create = StartupHelperCommand.TryParse(new[] { "--startup-helper", "create-task", "--mode", "tray" });
        Assert.IsNotNull(create);
        Assert.IsFalse(create!.RemoveTask);
        Assert.AreEqual(StartupWindowMode.Tray, create.WindowMode);
        Assert.AreEqual("--startup-helper create-task --mode tray", create.ToArguments());

        var remove = StartupHelperCommand.TryParse(new[] { "--startup-helper", "remove-task" });
        Assert.IsNotNull(remove);
        Assert.IsTrue(remove!.RemoveTask);

        Assert.IsNull(StartupHelperCommand.TryParse(new[] { "--startup-helper", "nonsense" }));
        Assert.IsNull(StartupHelperCommand.TryParse(new[] { "--other" }));
        Assert.IsNull(StartupHelperCommand.TryParse(Array.Empty<string>()));
    }

    [TestMethod]
    public void RestartForwardArgs_Whitelisted()
    {
        var forwarded = StartupHelperCommand.ForwardedRestartArguments(new[] { "--tray", "--evil", "--MINIMIZED", "--tray" });
        CollectionAssert.AreEquivalent(new[] { "--tray", "--minimized" }, forwarded.ToList());
    }

    [TestMethod]
    public void Onboarding_Matrix()
    {
        Assert.AreEqual((true, 0, true), OnboardingResolver.Resolve(false, false, -1), "Fresh install stamps in-progress and shows wizard.");
        Assert.AreEqual((true, null, false), OnboardingResolver.Resolve(false, false, 0), "Interrupted run resumes without rewrite.");
        Assert.AreEqual((false, null, false), OnboardingResolver.Resolve(false, false, 1), "Completed stays silent.");
        Assert.AreEqual((false, 1, true), OnboardingResolver.Resolve(true, false, -1), "Legacy settings upgrade silently.");
        Assert.AreEqual((false, 1, true), OnboardingResolver.Resolve(false, true, -1), "Legacy profiles-only upgrade silently.");
        Assert.AreEqual((false, null, false), OnboardingResolver.Resolve(true, true, 1), "Existing completed install never shows wizard.");
        Assert.AreEqual((true, null, false), OnboardingResolver.Resolve(true, false, 0), "Interrupted with settings resumes wizard.");
    }
}
