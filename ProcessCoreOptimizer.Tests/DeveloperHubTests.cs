using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProcessCoreOptimizer.Tests;

/// <summary>
/// Developer / Project Hub footer guard. The hub must open the four official
/// links through bound commands only (no hardcoded URLs in XAML), expose
/// localized automation names, hide the quick row while expanded, and keep
/// the real GitHub mark + coffee asset wired. About navigates in-app.
/// </summary>
[TestClass]
public sealed class DeveloperHubTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "version.txt")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "Repo root not found.");
        return dir!.FullName;
    }

    private static string ReadMainWindow() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "ProcessCoreOptimizer", "Views", "MainWindow.xaml"));

    [TestMethod]
    public void Footer_OpensLinksThroughCommands_NoHardcodedUrls()
    {
        string xaml = ReadMainWindow();
        foreach (string cmd in new[] { "OpenRepositoryCommand", "OpenAuthorCommand", "OpenWebsiteCommand", "OpenSupportCommand", "OpenAboutCommand" })
            Assert.IsTrue(xaml.Contains(cmd), $"MainWindow.xaml must bind {cmd}.");
        var urls = Regex.Matches(xaml, @"https?://[^\s""]+").Select(m => m.Value)
            .Where(u => !u.StartsWith("http://schemas.microsoft.com/", StringComparison.Ordinal))
            .ToList();
        Assert.AreEqual(0, urls.Count, "No hardcoded URLs allowed in MainWindow.xaml: " + string.Join(", ", urls));
    }

    [TestMethod]
    public void Footer_QuickRow_HiddenWhileExpanded()
    {
        string xaml = ReadMainWindow();
        Assert.IsTrue(xaml.Contains("FooterExpander"), "Hub expander must be named FooterExpander.");
        Assert.IsTrue(xaml.Contains("IsExpanded, ElementName=FooterExpander"),
            "Quick row visibility must gate on FooterExpander.IsExpanded.");
    }

    [TestMethod]
    public void Footer_RowButtons_HaveLocalizedAutomationNames()
    {
        string xaml = ReadMainWindow();
        foreach (string key in new[] { "StrRepoTooltip", "StrAuthorTooltip", "StrFooterWebsiteTooltip", "StrSupportTooltip", "StrSetGroupAbout" })
            Assert.IsTrue(xaml.Contains("AutomationProperties.Name=\"{DynamicResource " + key + "}\""),
                $"Footer row for {key} must expose a localized AutomationProperties.Name.");
    }

    [TestMethod]
    public void About_DoesNotOpenBrowser_NavigatesToSettings()
    {
        string vm = File.ReadAllText(Path.Combine(RepoRoot(), "ProcessCoreOptimizer", "ViewModels", "MainViewModel.cs"));
        Assert.IsTrue(vm.Contains("AboutRequested"), "MainViewModel must expose AboutRequested.");
        Assert.IsTrue(Regex.IsMatch(vm, @"NavigateToAbout\(\).*?SwitchTab\(""Settings""", RegexOptions.Singleline),
            "About navigation must switch to the Settings tab.");
        string codeBehind = File.ReadAllText(Path.Combine(RepoRoot(), "ProcessCoreOptimizer", "Views", "MainWindow.xaml.cs"));
        Assert.IsTrue(codeBehind.Contains("ScrollToAbout"), "MainWindow must scroll to the About section on AboutRequested.");
    }

    [TestMethod]
    public void GitHubMark_IsRealOctocatSilhouette()
    {
        string icons = File.ReadAllText(Path.Combine(RepoRoot(), "ProcessCoreOptimizer", "Themes", "Icons.xaml"));
        Assert.IsTrue(icons.Contains("M12,.297"), "IconGitHub must carry the real GitHub mark geometry.");
        Assert.IsTrue(icons.Contains("IconGlobe"), "A globe icon must exist for the website row.");
    }

    [TestMethod]
    public void CoffeeAsset_PresentAndReferenced()
    {
        Assert.IsTrue(File.Exists(Path.Combine(RepoRoot(), "ProcessCoreOptimizer", "Images", "buymeacoffee.png")),
            "buymeacoffee.png must exist in Images/.");
        Assert.IsTrue(ReadMainWindow().Contains("/Images/buymeacoffee.png"),
            "Footer must reference the coffee asset.");
    }
}
