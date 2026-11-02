using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class MutationTargetTests
{
    [TestMethod]
    public void TryCreate_RejectsIncompleteIdentity()
    {
        var t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.IsFalse(ProcessMutationTarget.TryCreate(0, t, "app", null, out _));
        Assert.IsFalse(ProcessMutationTarget.TryCreate(1234, default, "app", null, out _));
        Assert.IsFalse(ProcessMutationTarget.TryCreate(1234, t, "", null, out _));
        Assert.IsFalse(ProcessMutationTarget.TryCreate(1234, t, "   ", null, out _));
        Assert.IsFalse(ProcessMutationTarget.TryCreate(-5, t, "app", null, out _));

        Assert.IsTrue(ProcessMutationTarget.TryCreate(1234, t, "app", null, out var valid));
        Assert.IsTrue(valid.IsValid);
        Assert.IsFalse(default(ProcessMutationTarget).IsValid);
    }

    [TestMethod]
    public void RealService_InvalidTarget_RefusedWithoutMutation()
    {
        var svc = new ProcessService();
        using var self = Process.GetCurrentProcess();
        var invalid = default(ProcessMutationTarget);

        Assert.IsFalse(svc.SetPriority(self.Id, ProcessPriorityClass.Normal, invalid));
        Assert.IsFalse(svc.SetProcessAffinity(self.Id, 0x1, invalid));
        Assert.AreEqual("ERR_IDENTITY_MISMATCH",
            svc.ApplyCoreOptimization(self.Id, 0x1, OptimizationMode.Affinity, new Dictionary<int, uint>(), invalid));
    }

    [TestMethod]
    public void RealService_PidMismatch_Refused()
    {
        var svc = new ProcessService();
        using var self = Process.GetCurrentProcess();
        Assert.IsTrue(ProcessMutationTarget.TryCreate(
            self.Id + 1, self.StartTime.ToUniversalTime(), self.ProcessName, null, out var target));

        Assert.IsFalse(svc.SetPriority(self.Id, ProcessPriorityClass.Normal, target));
        Assert.AreEqual("ERR_IDENTITY_MISMATCH",
            svc.ApplyCoreOptimization(self.Id, 0x1, OptimizationMode.Affinity, new Dictionary<int, uint>(), target));
    }

    [TestMethod]
    public void RealService_DefaultStartTime_Refused()
    {
        var svc = new ProcessService();
        using var self = Process.GetCurrentProcess();
        var stale = new ProcessMutationTarget(self.Id, default, self.ProcessName, null);

        Assert.IsFalse(svc.SetPriority(self.Id, ProcessPriorityClass.Normal, stale));
        Assert.IsFalse(svc.SetProcessAffinity(self.Id, 0x1, stale));
    }

    [TestMethod]
    public void MatchesExpectedIdentity_InvalidTarget_AlwaysFalse()
    {
        using var self = Process.GetCurrentProcess();
        Assert.IsFalse(ProcessService.MatchesExpectedIdentity(self, default));
        Assert.IsFalse(ProcessService.MatchesExpectedIdentity(self,
            new ProcessMutationTarget(0, self.StartTime.ToUniversalTime(), self.ProcessName, null)));
    }

    [TestMethod]
    public void MatchesExpectedIdentity_ValidSelfTarget_Matches()
    {
        using var self = Process.GetCurrentProcess();
        Assert.IsTrue(ProcessMutationTarget.TryCreate(
            self.Id, self.StartTime.ToUniversalTime(), self.ProcessName, null, out var target));
        Assert.IsTrue(ProcessService.MatchesExpectedIdentity(self, target));
    }

    [TestMethod]
    public void MatchesExpectedIdentity_WrongNameOrPath_Rejects()
    {
        using var self = Process.GetCurrentProcess();
        var start = self.StartTime.ToUniversalTime();
        Assert.IsFalse(ProcessService.MatchesExpectedIdentity(self,
            new ProcessMutationTarget(self.Id, start, "definitely-not-this", null)));
        Assert.IsFalse(ProcessService.MatchesExpectedIdentity(self,
            new ProcessMutationTarget(self.Id, start, self.ProcessName, @"C:\nonexistent\other.exe")));
    }
}
