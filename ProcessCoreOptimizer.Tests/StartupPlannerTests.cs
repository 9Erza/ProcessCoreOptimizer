using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System.Linq;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class StartupPlannerTests
{
    private static DesiredStartupConfiguration Off() => new(false, StartupWindowMode.Normal, false);
    private static DesiredStartupConfiguration Normal(StartupWindowMode mode = StartupWindowMode.Normal) => new(true, mode, false);
    private static DesiredStartupConfiguration Elevated(StartupWindowMode mode = StartupWindowMode.Tray) => new(true, mode, true);

    private static RegistryStartupSnapshot NoRegistry(bool ok = true) =>
        new(false, null, string.Empty, false, false, false, ok, ok ? null : "read boom");

    private static ScheduledTaskStartupSnapshot NoTask(bool ok = true) =>
        new(false, false, null, string.Empty, false, false, false, false, false, ok, ok ? null : "read boom");

    private static RegistryStartupSnapshot GoodRegistry(string exe = @"C:\PCO\ProcessCoreOptimizer.exe", string args = "") =>
        new(true, exe, args, true, true, true);

    private static ScheduledTaskStartupSnapshot GoodTask(string exe = @"C:\PCO\ProcessCoreOptimizer.exe", string args = "--tray") =>
        new(true, true, exe, args, true, true, true, true, true);

    private static ActualStartupConfiguration Actual(RegistryStartupSnapshot reg, ScheduledTaskStartupSnapshot task) =>
        new(reg, task);

    [TestMethod] public void Disabled_NoRegistrations_StaysDisabled() =>
        Assert.AreEqual(StartupState.Disabled, StartupConfigurationPlanner.Evaluate(Off(), Actual(NoRegistry(), NoTask())).State);

    [TestMethod]
    public void Disabled_BothPresent_Conflict_RemovesBoth()
    {
        var eval = StartupConfigurationPlanner.Evaluate(Off(), Actual(GoodRegistry(), GoodTask()));
        Assert.AreEqual(StartupState.Conflict, eval.State);
        CollectionAssert.AreEquivalent(
            new[] { StartupOperation.RemoveRegistry, StartupOperation.RemoveScheduledTask },
            eval.RequiredOperations.ToList());
    }

    [TestMethod] public void Disabled_OnlyRegistry_Broken_RemovesIt() =>
        Assert.AreEqual(StartupState.Broken, StartupConfigurationPlanner.Evaluate(Off(), Actual(GoodRegistry(), NoTask())).State);

    [TestMethod]
    public void Normal_MissingRegistry_Broken_CreatesIt()
    {
        var eval = StartupConfigurationPlanner.Evaluate(Normal(), Actual(NoRegistry(), NoTask()));
        Assert.AreEqual(StartupState.Broken, eval.State);
        Assert.IsTrue(eval.Reasons.Contains(StartupReason.MissingRegistry));
        Assert.IsTrue(eval.RequiredOperations.Contains(StartupOperation.CreateOrUpdateRegistry));
    }

    [TestMethod]
    public void Normal_ValidRegistry_Registry()
    {
        var eval = StartupConfigurationPlanner.Evaluate(Normal(), Actual(GoodRegistry(), NoTask()));
        Assert.AreEqual(StartupState.Registry, eval.State);
        Assert.AreEqual(0, eval.RequiredOperations.Count);
    }

    [TestMethod]
    public void Normal_WrongExe_Broken_Recreates()
    {
        var reg = new RegistryStartupSnapshot(true, @"C:\Other\app.exe", "", true, false, true);
        var eval = StartupConfigurationPlanner.Evaluate(Normal(), Actual(reg, NoTask()));
        Assert.AreEqual(StartupState.Broken, eval.State);
        Assert.IsTrue(eval.Reasons.Contains(StartupReason.WrongExecutable));
        Assert.IsTrue(eval.RequiredOperations.Contains(StartupOperation.CreateOrUpdateRegistry));
    }

    [TestMethod]
    public void Normal_WrongArguments_Broken_Recreates()
    {
        var reg = new RegistryStartupSnapshot(true, @"C:\PCO\ProcessCoreOptimizer.exe", "--tray", true, true, false);
        var eval = StartupConfigurationPlanner.Evaluate(Normal(StartupWindowMode.Normal), Actual(reg, NoTask()));
        Assert.AreEqual(StartupState.Broken, eval.State);
        Assert.IsTrue(eval.Reasons.Contains(StartupReason.WrongArguments));
    }

    [TestMethod]
    public void WindowMode_Changes_Arguments()
    {
        Assert.AreEqual("", new DesiredStartupConfiguration(true, StartupWindowMode.Normal, false).Arguments);
        Assert.AreEqual("--minimized", new DesiredStartupConfiguration(true, StartupWindowMode.Minimized, false).Arguments);
        Assert.AreEqual("--tray", new DesiredStartupConfiguration(true, StartupWindowMode.Tray, false).Arguments);
    }

    [TestMethod]
    public void Elevated_MissingTask_Broken_CreatesFirst()
    {
        var eval = StartupConfigurationPlanner.Evaluate(Elevated(), Actual(NoRegistry(), NoTask()));
        Assert.AreEqual(StartupState.Broken, eval.State);
        Assert.IsTrue(eval.Reasons.Contains(StartupReason.MissingScheduledTask));
        Assert.AreEqual(StartupOperation.CreateOrUpdateScheduledTask, eval.RequiredOperations[0]);
    }

    [TestMethod]
    public void Elevated_ValidTask_ElevatedTask()
    {
        var eval = StartupConfigurationPlanner.Evaluate(Elevated(), Actual(NoRegistry(), GoodTask()));
        Assert.AreEqual(StartupState.ElevatedTask, eval.State);
    }

    [TestMethod]
    public void Elevated_DisabledTask_Broken()
    {
        var task = GoodTask() with { Enabled = false };
        var eval = StartupConfigurationPlanner.Evaluate(Elevated(), Actual(NoRegistry(), task));
        Assert.AreEqual(StartupState.Broken, eval.State);
        Assert.IsTrue(eval.Reasons.Contains(StartupReason.TaskDisabled));
    }

    [TestMethod]
    public void Elevated_WithoutLogonTrigger_Broken()
    {
        var task = GoodTask() with { HasLogonTrigger = false };
        var eval = StartupConfigurationPlanner.Evaluate(Elevated(), Actual(NoRegistry(), task));
        Assert.IsTrue(eval.Reasons.Contains(StartupReason.MissingLogonTrigger));
    }

    [TestMethod]
    public void Elevated_WithoutRunLevel_Broken()
    {
        var task = GoodTask() with { IsElevated = false };
        var eval = StartupConfigurationPlanner.Evaluate(Elevated(), Actual(NoRegistry(), task));
        Assert.IsTrue(eval.Reasons.Contains(StartupReason.MissingElevatedRunLevel));
    }

    [TestMethod]
    public void Elevated_RegistryLeftOver_Conflict_CreateBeforeDelete()
    {
        var eval = StartupConfigurationPlanner.Evaluate(Elevated(), Actual(GoodRegistry(), NoTask()));
        Assert.AreEqual(StartupState.Broken, eval.State);
        Assert.IsTrue(eval.Reasons.Contains(StartupReason.UnexpectedRegistryEntry));
        // Safe order: the new channel is established before the stale one is removed.
        Assert.AreEqual(StartupOperation.CreateOrUpdateScheduledTask, eval.RequiredOperations[0]);
        Assert.IsTrue(eval.RequiredOperations.Contains(StartupOperation.RemoveRegistry));
    }

    [TestMethod]
    public void Normal_TaskLeftOver_Broken_CreateBeforeDelete()
    {
        var eval = StartupConfigurationPlanner.Evaluate(Normal(), Actual(NoRegistry(), GoodTask("--tray")));
        Assert.AreEqual(StartupState.Broken, eval.State);
        Assert.IsTrue(eval.Reasons.Contains(StartupReason.UnexpectedScheduledTask));
        Assert.AreEqual(StartupOperation.CreateOrUpdateRegistry, eval.RequiredOperations[0]);
        Assert.IsTrue(eval.RequiredOperations.Contains(StartupOperation.RemoveScheduledTask));
    }

    [TestMethod]
    public void BothPresent_Conflict_ResolvesTowardDesired()
    {
        var toElevated = StartupConfigurationPlanner.Evaluate(Elevated(), Actual(GoodRegistry(), GoodTask()));
        Assert.AreEqual(StartupState.Conflict, toElevated.State);
        Assert.IsTrue(toElevated.RequiredOperations.Contains(StartupOperation.RemoveRegistry));

        var toNormal = StartupConfigurationPlanner.Evaluate(Normal(), Actual(GoodRegistry(), GoodTask()));
        Assert.AreEqual(StartupState.Conflict, toNormal.State);
        Assert.IsTrue(toNormal.RequiredOperations.Contains(StartupOperation.RemoveScheduledTask));
    }

    [TestMethod]
    public void MissingExecutable_Reported()
    {
        var reg = GoodRegistry() with { ExecutableExists = false };
        var eval = StartupConfigurationPlanner.Evaluate(Normal(), Actual(reg, NoTask()));
        Assert.IsTrue(eval.Reasons.Contains(StartupReason.ExecutableMissing));
        Assert.AreEqual(StartupState.Broken, eval.State);
    }

    [TestMethod]
    public void ReadFailure_IsBroken_NoOperations()
    {
        var eval = StartupConfigurationPlanner.Evaluate(Normal(), Actual(NoRegistry(false), NoTask()));
        Assert.AreEqual(StartupState.Broken, eval.State);
        Assert.IsTrue(eval.Reasons.Contains(StartupReason.ReadFailed));
        Assert.AreEqual(0, eval.RequiredOperations.Count);
    }

    [TestMethod]
    public void Parser_HandlesQuotedAndBareCommands()
    {
        var (exe, args) = StartupCommandParser.Parse("\"C:\\PCO\\app.exe\" --tray");
        Assert.AreEqual("C:\\PCO\\app.exe", exe);
        Assert.AreEqual("--tray", args);
        var (exe2, args2) = StartupCommandParser.Parse("C:\\PCO\\app.exe --minimized");
        Assert.AreEqual("C:\\PCO\\app.exe", exe2);
        Assert.AreEqual("--minimized", args2);
        var (exe3, args3) = StartupCommandParser.Parse(null);
        Assert.IsNull(exe3);
        Assert.AreEqual("", args3);
    }

    [TestMethod]
    public void Migration_LegacyKeys_MoveOnce()
    {
        var settings = new AppSettings
        {
            LegacyStartMinimized = true,
            LegacyRunAsAdministrator = true,
            StartupSettingsVersion = 0
        };
        StartupSettingsMigration.Apply(settings);
        Assert.AreEqual(StartupWindowMode.Minimized, settings.StartupWindowMode);
        Assert.IsTrue(settings.StartupRunElevated);
        Assert.AreEqual(1, settings.StartupSettingsVersion);
        Assert.IsNull(settings.LegacyStartMinimized);
        Assert.IsNull(settings.LegacyRunAsAdministrator);
        // Idempotent: second run keeps user choices.
        settings.StartupWindowMode = StartupWindowMode.Tray;
        StartupSettingsMigration.Apply(settings);
        Assert.AreEqual(StartupWindowMode.Tray, settings.StartupWindowMode);
    }
}
