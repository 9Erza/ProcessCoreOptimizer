using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Diagnostics;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class ExternalLinksTests
{
    [TestMethod]
    public void Allowlist_ContainsExactlyTheThreeOfficialUrls()
    {
        Assert.AreEqual("https://github.com/9Erza", ExternalLinks.GetUrl(PcoExternalLink.Author));
        Assert.AreEqual("https://github.com/9Erza/ProcessCoreOptimizer", ExternalLinks.GetUrl(PcoExternalLink.Repository));
        Assert.AreEqual("https://buymeacoffee.com/9erza", ExternalLinks.GetUrl(PcoExternalLink.Support));
    }

    [TestMethod]
    public void IsAllowed_AcceptsOnlyExactOfficialUrls()
    {
        Assert.IsTrue(ExternalLinks.IsAllowed("https://github.com/9Erza"));
        Assert.IsTrue(ExternalLinks.IsAllowed("https://github.com/9Erza/ProcessCoreOptimizer"));
        Assert.IsTrue(ExternalLinks.IsAllowed("https://buymeacoffee.com/9erza"));
        Assert.IsFalse(ExternalLinks.IsAllowed(null));
        Assert.IsFalse(ExternalLinks.IsAllowed(""));
        Assert.IsFalse(ExternalLinks.IsAllowed("https://github.com/ErykZ"));
        Assert.IsFalse(ExternalLinks.IsAllowed("https://github.com/9Erza/FrameHub"));
        Assert.IsFalse(ExternalLinks.IsAllowed("https://buymeacoffee.com/other"));
        Assert.IsFalse(ExternalLinks.IsAllowed("javascript:alert(1)"));
        Assert.IsFalse(ExternalLinks.IsAllowed("file:///etc/passwd"));
        Assert.IsFalse(ExternalLinks.IsAllowed("https://github.com/9Erza?tab=repositories"));
        Assert.IsFalse(ExternalLinks.IsAllowed("http://github.com/9Erza"));
    }

    [TestMethod]
    public void TryOpen_UsesLauncher_NoDirectProcessStart()
    {
        string? launched = null;
        var previous = ExternalLinks.Launcher;
        try
        {
            ExternalLinks.Launcher = psi => { launched = psi.FileName; return true; };
            Assert.IsTrue(ExternalLinks.TryOpen(PcoExternalLink.Repository));
            Assert.AreEqual("https://github.com/9Erza/ProcessCoreOptimizer", launched);
        }
        finally
        {
            ExternalLinks.Launcher = previous;
        }
    }

    [TestMethod]
    public void TryOpenUrl_RefusesNonAllowlisted_WithoutLaunching()
    {
        bool launched = false;
        var previous = ExternalLinks.Launcher;
        try
        {
            ExternalLinks.Launcher = psi => { launched = true; return true; };
            Assert.IsFalse(ExternalLinks.TryOpenUrl("https://evil.example/"));
            Assert.IsFalse(launched, "Non-allowlisted URLs must never reach the launcher.");
            Assert.IsTrue(ExternalLinks.TryOpenUrl("https://buymeacoffee.com/9erza"));
            Assert.IsTrue(launched);
        }
        finally
        {
            ExternalLinks.Launcher = previous;
        }
    }

    [TestMethod]
    public void TryOpen_LauncherFailure_ReturnsFalseWithoutThrowing()
    {
        var previous = ExternalLinks.Launcher;
        try
        {
            ExternalLinks.Launcher = psi => throw new InvalidOperationException("no browser");
            Assert.IsFalse(ExternalLinks.TryOpen(PcoExternalLink.Author));
            Assert.IsFalse(ExternalLinks.TryOpenUrl(ExternalLinks.AuthorUrl));
        }
        finally
        {
            ExternalLinks.Launcher = previous;
        }
    }
}
