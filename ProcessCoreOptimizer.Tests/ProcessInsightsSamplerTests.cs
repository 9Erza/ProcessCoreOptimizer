using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Linq;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class ProcessInsightsSamplerTests
{
    [TestMethod]
    public void FirstSample_HasUnknownCpuButKnownMemory()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var sampler = new ProcessInsightsSampler(capacity: 10,
            reader: _ => (true, start, TimeSpan.FromSeconds(5), 50L * 1024 * 1024, 40L * 1024 * 1024));

        var r = sampler.Sample(1234);
        Assert.IsTrue(r.Readable);
        Assert.IsNull(r.CpuPercent, "First CPU point must be Unknown (no baseline yet).");
        Assert.AreEqual(50L * 1024 * 1024, r.WorkingSetBytes);
        Assert.AreEqual(40L * 1024 * 1024, r.PrivateBytes);
        Assert.AreEqual(1, sampler.History.Count);
    }

    [TestMethod]
    public void FailedRead_ProducesUnreadablePoint()
    {
        var sampler = new ProcessInsightsSampler(10, reader: _ => (false, default, default, 0, 0));
        var r = sampler.Sample(9999);
        Assert.IsFalse(r.Readable);
        Assert.IsNull(r.CpuPercent);
        Assert.IsNull(r.WorkingSetBytes);
    }

    [TestMethod]
    public void RingBuffer_IsBounded()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var sampler = new ProcessInsightsSampler(capacity: 10, reader: _ => (true, start, TimeSpan.Zero, 1, 1));
        for (int i = 0; i < 25; i++) sampler.Sample(5);
        Assert.AreEqual(10, sampler.History.Count);
    }

    [TestMethod]
    public void RecycledPid_ResetsBaseline()
    {
        DateTime start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var sampler = new ProcessInsightsSampler(10,
            reader: _ => (true, start, TimeSpan.FromSeconds(5), 10, 10));

        sampler.Sample(77);
        // Same PID, different start time = recycled PID: baseline resets, CPU Unknown again.
        start = start.AddHours(1);
        var r = sampler.Sample(77);
        Assert.IsTrue(r.Readable);
        Assert.IsNull(r.CpuPercent);
    }

    [TestMethod]
    public void ComputeCpuPercent_MatchesScannerMath()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 2, DateTimeKind.Utc);
        var last = now.AddSeconds(-2);
        double? pct = ProcessInsightsSampler.ComputeCpuPercent(
            TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2), now, last);
        double expected = (2000.0 / (2 * 1000 * Math.Max(Environment.ProcessorCount, 1))) * 100;
        Assert.AreEqual(expected, pct!.Value, 1e-9);
        Assert.IsNull(ProcessInsightsSampler.ComputeCpuPercent(TimeSpan.Zero, TimeSpan.Zero, now, now));
    }
}
