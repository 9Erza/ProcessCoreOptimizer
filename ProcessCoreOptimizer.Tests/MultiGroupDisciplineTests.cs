using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Services;
using System.Collections.Generic;
using System.Linq;

namespace ProcessCoreOptimizer.Tests;

/// <summary>
/// Synthetic multi-group discipline: without >64-LP hardware, only the pure
/// mapping/validation rules are pinned. Real multi-group hardware: NOT RUN.
/// </summary>
[TestClass]
public sealed class MultiGroupDisciplineTests
{
    private static Dictionary<int, uint> BigMap()
    {
        var map = new Dictionary<int, uint>();
        for (int i = 0; i < 128; i++) map[i] = (uint)(1000 + i);
        return map;
    }

    [TestMethod]
    public void KnownMask_NeverExceeds64Bits()
    {
        long mask = ProcessService.KnownProcessorMask(BigMap());
        Assert.AreEqual(-1L, mask, "Group-0 model exposes exactly the low 64 bits.");
    }

    [TestMethod]
    public void HighBits_AreRejected_NotSilentlyDropped()
    {
        var map8 = Enumerable.Range(0, 8).ToDictionary(i => i, i => (uint)(100 + i));
        Assert.AreEqual("ERR_MASK_OUT_OF_RANGE", ProcessService.ValidateCoreMask(1L << 10, map8));
        // Bits >= 64 cannot even be expressed in a 64-bit mask (C# shift wraps),
        // so the group-0 limit is enforced by the type system itself.
        Assert.AreEqual("ERR_MASK_OUT_OF_RANGE", ProcessService.ValidateCoreMask(1L << 40, map8));
        Assert.IsNull(ProcessService.ValidateCoreMask(1L << 63, BigMap()));
    }

    [TestMethod]
    public void CpuSetConversion_MapsOnlyKnownBits()
    {
        var sparse = new Dictionary<int, uint> { [0] = 10, [63] = 63 };
        uint[] ids = ProcessService.ConvertMaskToCpuSetIds((1L << 5) | (1L << 0) | (1L << 63), sparse);
        CollectionAssert.AreEquivalent(new[] { 10u, 63u }, ids);
    }
}
