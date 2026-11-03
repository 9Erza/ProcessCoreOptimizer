using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Services;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class PriorityRankTests
{
    [TestMethod]
    public void Rank_OrdersSemantically()
    {
        Assert.IsTrue(PriorityService.Rank("Idle") < PriorityService.Rank("BelowNormal"));
        Assert.IsTrue(PriorityService.Rank("BelowNormal") < PriorityService.Rank("Normal"));
        Assert.IsTrue(PriorityService.Rank("Normal") < PriorityService.Rank("AboveNormal"));
        Assert.IsTrue(PriorityService.Rank("AboveNormal") < PriorityService.Rank("High"));
        Assert.IsTrue(PriorityService.Rank("High") < PriorityService.Rank("RealTime"));
    }

    [TestMethod]
    public void Rank_AcceptsTranslationsAndUnknowns()
    {
        Assert.AreEqual(PriorityService.Rank("Normal"), PriorityService.Rank("Normalny"));
        Assert.AreEqual(PriorityService.Rank("High"), PriorityService.Rank("Wysoki"));
        Assert.AreEqual(PriorityService.Rank("Real Time"), PriorityService.Rank("Czas Rzeczywisty"));
        Assert.AreEqual(PriorityService.Rank("Normal"), PriorityService.Rank("gibberish"));
        Assert.AreEqual(PriorityService.Rank("Normal"), PriorityService.Rank(null));
    }
}
