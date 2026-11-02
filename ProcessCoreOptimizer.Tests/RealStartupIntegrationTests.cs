using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ProcessCoreOptimizer.Tests;

/// <summary>
/// Real Windows integration for startup registration. Uses unique
/// PcoTestProbe_* registry values and task names, always cleaned up.
/// Runs only with PCO_REAL_STARTUP_TESTS=1; otherwise Inconclusive.
/// Never touches the production PCO registration or other software.
/// </summary>
[TestClass]
public sealed class RealStartupIntegrationTests
{
    private string _valueName = string.Empty;
    private string _taskName = string.Empty;
    private StartupService? _svc;

    [TestInitialize]
    public void GuardAndSetup()
    {
        if (Environment.GetEnvironmentVariable("PCO_REAL_STARTUP_TESTS") != "1")
        {
            Assert.Inconclusive("Set PCO_REAL_STARTUP_TESTS=1 to run real startup integration.");
        }

        string tag = Guid.NewGuid().ToString("N")[..8];
        _valueName = $"PcoTestProbe_{tag}";
        _taskName = $"PcoTestProbe_{tag}";
        _svc = new StartupService(_valueName, _taskName);
    }

    [TestCleanup]
    public void CleanupProbeRegistrations()
    {
        if (_svc == null) return;
        try
        {
            var off = new DesiredStartupConfiguration(false, StartupWindowMode.Normal, false);
            var coordinator = new StartupApplyCoordinator(new StartupConfigurationExecutor(_svc));
            coordinator.ApplyLatestAsync(off).GetAwaiter().GetResult();
        }
        catch { }
        _svc.Dispose();
    }

    [TestMethod]
    public async Task Real_ReadActual_OnCleanMachine_IsDisabled()
    {
        var desired = new DesiredStartupConfiguration(false, StartupWindowMode.Normal, false);
        var actual = await _svc!.ReadActualAsync(desired);
        Assert.IsFalse(actual.Registry.Exists);
        Assert.IsFalse(actual.Task.Exists);
        var eval = StartupConfigurationPlanner.Evaluate(desired, actual);
        Assert.AreEqual(StartupState.Disabled, eval.State);
    }

    [TestMethod]
    public async Task Real_NormalStartup_WriteVerifyRemove()
    {
        var coordinator = new StartupApplyCoordinator(new StartupConfigurationExecutor(_svc!));
        var on = new DesiredStartupConfiguration(true, StartupWindowMode.Minimized, false);
        var applied = await coordinator.ApplyLatestAsync(on);
        Assert.IsTrue(applied.Success, applied.Error);
        Assert.AreEqual(StartupState.Registry, applied.FinalEvaluation.State);

        var off = new DesiredStartupConfiguration(false, StartupWindowMode.Normal, false);
        var removed = await coordinator.ApplyLatestAsync(off);
        Assert.IsTrue(removed.Success, removed.Error);
        Assert.AreEqual(StartupState.Disabled, removed.FinalEvaluation.State);
    }

    [TestMethod]
    public async Task Real_ComQuery_ParsesOwnTask()
    {
        var coordinator = new StartupApplyCoordinator(new StartupConfigurationExecutor(_svc!));
        var elevated = new DesiredStartupConfiguration(true, StartupWindowMode.Tray, true);
        var applied = await coordinator.ApplyLatestAsync(elevated);
        // Elevated create needs admin or UAC; on a standard session this fails cleanly.
        // Either outcome is informative; the task must never be left half-broken silently.
        var reread = await _svc!.ReadActualAsync(elevated);
        if (applied.Success)
        {
            Assert.AreEqual(StartupState.ElevatedTask, applied.FinalEvaluation.State);
            Assert.IsTrue(reread.Task.HasLogonTrigger);
            Assert.IsTrue(reread.Task.IsElevated);
        }
        else
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(applied.Error));
            TestContext?.WriteLine($"Elevated path unavailable here: {applied.Error}");
        }

        var off = new DesiredStartupConfiguration(false, StartupWindowMode.Normal, false);
        var removed = await coordinator.ApplyLatestAsync(off);
        Assert.AreEqual(StartupState.Disabled, removed.FinalEvaluation.State, removed.Error ?? string.Join(",", removed.FinalEvaluation.Reasons.Select(r => r.ToString())));
    }

    public TestContext? TestContext { get; set; }
}
