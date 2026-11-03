using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Services;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class ProfileStudioValidatorTests
{
    [TestMethod]
    public void Validate_RequiresName()
    {
        var r = ProfileStudioValidator.Validate("  ", "FF", true, true);
        Assert.IsFalse(r.Ok);
        Assert.IsFalse(string.IsNullOrEmpty(r.ErrorEn));
        Assert.IsFalse(string.IsNullOrEmpty(r.ErrorPl));
    }

    [TestMethod]
    public void Validate_RequiresAtLeastOneAction()
    {
        var r = ProfileStudioValidator.Validate("myapp", "FF", false, false);
        Assert.IsFalse(r.Ok);
    }

    [TestMethod]
    public void Validate_RequiresNonZeroMaskWhenCore()
    {
        Assert.IsFalse(ProfileStudioValidator.Validate("myapp", "", true, true).Ok);
        Assert.IsFalse(ProfileStudioValidator.Validate("myapp", "0", true, true).Ok);
        Assert.IsFalse(ProfileStudioValidator.Validate("myapp", "ZZZ", true, true).Ok);
    }

    [TestMethod]
    public void Validate_AcceptsHexForms()
    {
        var plain = ProfileStudioValidator.Validate("myapp", "FF", true, true);
        var prefixed = ProfileStudioValidator.Validate("myapp", "0xFF", true, true);
        Assert.IsTrue(plain.Ok && plain.Mask == 0xFF);
        Assert.IsTrue(prefixed.Ok && prefixed.Mask == 0xFF);
    }

    [TestMethod]
    public void Validate_PriorityOnly_SkipsMask()
    {
        var r = ProfileStudioValidator.Validate("myapp", "", false, true);
        Assert.IsTrue(r.Ok);
        Assert.AreEqual(0, r.Mask);
    }

    [TestMethod]
    public void TryParseMask_HandlesForms()
    {
        Assert.IsTrue(ProfileStudioValidator.TryParseMask("0x1A", out long m) && m == 0x1A);
        Assert.IsTrue(ProfileStudioValidator.TryParseMask("ff", out _));
        Assert.IsFalse(ProfileStudioValidator.TryParseMask(null, out _));
        Assert.IsFalse(ProfileStudioValidator.TryParseMask("", out _));
        Assert.IsFalse(ProfileStudioValidator.TryParseMask("xyz", out _));
    }
}
