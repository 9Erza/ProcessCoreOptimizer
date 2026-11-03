using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class StartupWindowBehaviorTests
{
    [TestMethod]
    public void NormalSetting_NoFlags_ShowsNormal()
    {
        Assert.AreEqual(StartupWindowAction.ShowNormal,
            StartupWindowBehavior.Resolve(StartupWindowMode.Normal, false, false));
    }

    [TestMethod]
    public void MinimizedSetting_StaysOnTaskbar()
    {
        // MinimizeToTray must NOT reinterpret the explicit Minimized choice.
        Assert.AreEqual(StartupWindowAction.ShowMinimized,
            StartupWindowBehavior.Resolve(StartupWindowMode.Minimized, false, false));
    }

    [TestMethod]
    public void TraySetting_AlwaysHides()
    {
        Assert.AreEqual(StartupWindowAction.HideToTray,
            StartupWindowBehavior.Resolve(StartupWindowMode.Tray, false, false));
    }

    [TestMethod]
    public void TrayFlag_OverridesNormalAndMinimized()
    {
        Assert.AreEqual(StartupWindowAction.HideToTray,
            StartupWindowBehavior.Resolve(StartupWindowMode.Normal, false, true));
        Assert.AreEqual(StartupWindowAction.HideToTray,
            StartupWindowBehavior.Resolve(StartupWindowMode.Minimized, false, true));
        Assert.AreEqual(StartupWindowAction.HideToTray,
            StartupWindowBehavior.Resolve(StartupWindowMode.Normal, true, true));
    }

    [TestMethod]
    public void MinimizedFlag_RespectedWithoutTray()
    {
        Assert.AreEqual(StartupWindowAction.ShowMinimized,
            StartupWindowBehavior.Resolve(StartupWindowMode.Normal, true, false));
    }

    [TestMethod]
    public void TraySetting_WinsOverMinimizedFlag()
    {
        Assert.AreEqual(StartupWindowAction.HideToTray,
            StartupWindowBehavior.Resolve(StartupWindowMode.Tray, true, false));
    }
}
