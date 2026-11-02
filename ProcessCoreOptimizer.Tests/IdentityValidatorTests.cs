using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class IdentityValidatorTests
{
    private static ProcessProfile NameProfile(string name, string? path = null) => new()
    {
        ProcessName = name,
        ExecutablePath = path,
        AffinityMask = 0x1,
        Priority = "Normal",
        ApplyPriority = true,
        ApplyCoreOptimization = false,
        IsEnabled = true
    };

    [TestMethod]
    public void MatchesCore_AllFieldsMatch_ReturnsTrue()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        bool ok = ProcessIdentityValidator.MatchesIdentityCore(
            1234, start, "notepad", @"C:\Windows\notepad.exe",
            1234, start, "notepad", @"C:\Windows\notepad.exe");
        Assert.IsTrue(ok);
    }

    [TestMethod]
    public void MatchesCore_RecycledPid_DifferentStartTime_Rejects()
    {
        var t1 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddSeconds(5);
        bool ok = ProcessIdentityValidator.MatchesIdentityCore(
            1234, t1, "notepad", null,
            1234, t2, "notepad", null);
        Assert.IsFalse(ok, "PID reuse with different start time must be rejected");
    }

    [TestMethod]
    public void MatchesCore_NameMismatch_Rejects()
    {
        var t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        bool ok = ProcessIdentityValidator.MatchesIdentityCore(
            1234, t, "notepad", null,
            1234, t, "calc", null);
        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void MatchesCore_ExeSuffixNormalized()
    {
        var t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        bool ok = ProcessIdentityValidator.MatchesIdentityCore(
            1234, t, "notepad.exe", null,
            1234, t, "notepad", null);
        Assert.IsTrue(ok);
    }

    [TestMethod]
    public void MatchesCore_PathMismatch_Rejects_NoFallback()
    {
        var t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        bool ok = ProcessIdentityValidator.MatchesIdentityCore(
            1234, t, "app", @"C:\Apps\app.exe",
            1234, t, "app", @"C:\Other\app.exe");
        Assert.IsFalse(ok, "Path-bound profile must never fall back to same-name executable");
    }

    [TestMethod]
    public void MatchesCore_MissingStartTime_Refuses()
    {
        var t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.IsFalse(ProcessIdentityValidator.MatchesIdentityCore(1, default, "a", null, 1, t, "a", null));
        Assert.IsFalse(ProcessIdentityValidator.MatchesIdentityCore(1, t, "a", null, 1, default, "a", null));
    }

    [TestMethod]
    public void MatchesProfile_PathBound_RequiresExactPath()
    {
        var pathBound = NameProfile("app", @"C:\Apps\app.exe");
        Assert.IsTrue(ProfileService.MatchesIdentity(pathBound, "app", @"C:\Apps\app.exe"));
        Assert.IsFalse(ProfileService.MatchesIdentity(pathBound, "app", @"C:\Other\app.exe"));
        Assert.IsFalse(ProfileService.MatchesIdentity(pathBound, "app", null));

        var legacy = NameProfile("app");
        Assert.IsTrue(ProfileService.MatchesIdentity(legacy, "app", @"C:\Anything\app.exe"));
        Assert.IsTrue(ProfileService.MatchesIdentity(legacy, "app.exe", null));
    }

    [TestMethod]
    public void LiveProcess_CurrentProcess_MatchesOwnIdentity()
    {
        using var self = Process.GetCurrentProcess();
        var key = new ProcessInstanceKey(self.Id, self.StartTime.ToUniversalTime());
        var profile = NameProfile(self.ProcessName);
        Assert.IsTrue(ProcessIdentityValidator.MatchesLiveProcess(key, self.ProcessName, profile));
    }

    [TestMethod]
    public void LiveProcess_StaleStartTime_Rejects()
    {
        using var self = Process.GetCurrentProcess();
        var staleKey = new ProcessInstanceKey(self.Id, self.StartTime.ToUniversalTime().AddSeconds(-30));
        var profile = NameProfile(self.ProcessName);
        Assert.IsFalse(ProcessIdentityValidator.MatchesLiveProcess(staleKey, self.ProcessName, profile));
    }

    [TestMethod]
    public void LiveProcess_WrongName_Rejects()
    {
        using var self = Process.GetCurrentProcess();
        var key = new ProcessInstanceKey(self.Id, self.StartTime.ToUniversalTime());
        var profile = NameProfile("definitely-not-this-process-xyz");
        Assert.IsFalse(ProcessIdentityValidator.MatchesLiveProcess(key, self.ProcessName, profile));
    }
}
