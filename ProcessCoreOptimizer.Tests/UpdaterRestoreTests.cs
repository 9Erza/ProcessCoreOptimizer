using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using ProcessCoreOptimizer.WPF.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class UpdaterTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _route;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> route) => _route = route;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_route(request));
    }

    private static UpdateService ServiceFor(string versionTxt, Func<string, HttpResponseMessage> tagRoute, HttpStatusCode versionStatus = HttpStatusCode.OK)
    {
        var svc = new UpdateService();
        svc.HttpClientFactory = () => new HttpClient(new FakeHandler(req =>
        {
            string url = req.RequestUri?.ToString() ?? string.Empty;
            if (url.Contains("version.txt"))
            {
                if (versionStatus != HttpStatusCode.OK)
                    return new HttpResponseMessage(versionStatus);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(versionTxt, Encoding.UTF8) };
            }
            if (url.Contains("releases/tags/"))
            {
                string tag = url.Substring(url.LastIndexOf('/') + 1);
                return tagRoute(tag);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        return svc;
    }

    private static HttpResponseMessage TagJson(string json) =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string ReleaseExact200 = """{"tag_name":"v2.0.0","html_url":"https://example/r","draft":false,"prerelease":false,"assets":[{"name":"ProcessCoreOptimizer_Setup_2.0.0.exe"}]}""";
    private const string ReleaseNoAsset = """{"tag_name":"v2.0.0","html_url":"https://example/r","draft":false,"prerelease":false,"assets":[]}""";
    private const string ReleaseOld = """{"tag_name":"v1.3.0","html_url":"https://example/r","draft":false,"prerelease":false,"assets":[{"name":"ProcessCoreOptimizer_Setup_1.3.0.exe"}]}""";

    [TestMethod]
    public async Task NewerVersionWithAsset_Notifies()
    {
        var svc = ServiceFor("2.0.0", _ => TagJson(ReleaseExact200));
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsTrue(result.IsUpdateAvailable);
        Assert.AreEqual("2.0.0", result.LatestVersion);
    }

    [TestMethod]
    public async Task VersionAheadButNoPublishedRelease_DoesNotNotify()
    {
        var svc = ServiceFor("2.0.0", _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsFalse(result.IsUpdateAvailable, "No false positive before the release is published.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Note));
    }

    [TestMethod]
    public async Task ReleaseWithoutInstallerAsset_DoesNotNotify()
    {
        var svc = ServiceFor("2.0.0", _ => TagJson(ReleaseNoAsset));
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsFalse(result.IsUpdateAvailable);
    }

    [TestMethod]
    public async Task SameVersion_DoesNotNotify()
    {
        var svc = ServiceFor("1.3.0", _ => TagJson(ReleaseOld));
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsFalse(result.IsUpdateAvailable);
    }

    [TestMethod]
    public async Task PublishedReleaseOlderThanCurrent_NoDowngradePrompt()
    {
        var svc = ServiceFor("2.1.0", _ => TagJson(ReleaseOld));
        var result = await svc.CheckForUpdatesAsync("2.0.0");
        Assert.IsFalse(result.IsUpdateAvailable, "An older published release must never prompt a downgrade.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Note));
    }

    [TestMethod]
    public async Task UnparsableReleaseTag_DoesNotNotify()
    {
        const string weird = """{"tag_name":"nightly-xyz","html_url":"https://example/r","assets":[{"name":"a.zip"}]}""";
        var svc = ServiceFor("2.0.0", _ => TagJson(weird));
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsFalse(result.IsUpdateAvailable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Note));
    }

    private const string ReleaseDraft200 = """{"tag_name":"v2.0.0","html_url":"https://example/r","draft":true,"prerelease":false,"assets":[{"name":"ProcessCoreOptimizer_Setup_2.0.0.exe"}]}""";
    private const string ReleasePrerelease200 = """{"tag_name":"v2.0.0","html_url":"https://example/r","draft":false,"prerelease":true,"assets":[{"name":"ProcessCoreOptimizer_Setup_2.0.0.exe"}]}""";
    private const string ReleaseZipOnly = """{"tag_name":"v2.0.0","html_url":"https://example/r","draft":false,"prerelease":false,"assets":[{"name":"files.zip"}]}""";
    private const string ReleaseOtherExe = """{"tag_name":"v2.0.0","html_url":"https://example/r","draft":false,"prerelease":false,"assets":[{"name":"Other_Setup.exe"}]}""";
    private const string ReleaseTagMismatch = """{"tag_name":"v9.9.9","html_url":"https://example/r","draft":false,"prerelease":false,"assets":[{"name":"ProcessCoreOptimizer_Setup_2.0.0.exe"}]}""";

    [TestMethod]
    public async Task DraftRelease_DoesNotNotify()
    {
        var svc = ServiceFor("2.0.0", _ => TagJson(ReleaseDraft200));
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsFalse(result.IsUpdateAvailable);
    }

    [TestMethod]
    public async Task PrereleaseRelease_NoStableNotification()
    {
        var svc = ServiceFor("2.0.0", _ => TagJson(ReleasePrerelease200));
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsFalse(result.IsUpdateAvailable);
    }

    [TestMethod]
    public async Task PublishedZipOnly_DoesNotNotify()
    {
        var svc = ServiceFor("2.0.0", _ => TagJson(ReleaseZipOnly));
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsFalse(result.IsUpdateAvailable);
    }

    [TestMethod]
    public async Task PublishedUnrelatedExe_DoesNotNotify()
    {
        var svc = ServiceFor("2.0.0", _ => TagJson(ReleaseOtherExe));
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsFalse(result.IsUpdateAvailable, "Only the exact Setup name counts as an installer.");
    }

    [TestMethod]
    public async Task ExactSetupName_NotifiesWithTagReleaseUrl()
    {
        var svc = ServiceFor("2.0.0", _ => TagJson(ReleaseExact200));
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsTrue(result.IsUpdateAvailable);
        Assert.AreEqual("https://github.com/9Erza/ProcessCoreOptimizer/releases/tag/v2.0.0", result.ReleaseUrl);
    }

    [TestMethod]
    public async Task TagMismatch_DoesNotNotify()
    {
        var svc = ServiceFor("2.0.0", _ => TagJson(ReleaseTagMismatch));
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsFalse(result.IsUpdateAvailable);
    }

    [TestMethod]
    public async Task VersionEndpoint404_ReportsErrorWithoutUpdate()
    {
        var svc = ServiceFor("2.0.0", _ => TagJson(ReleaseExact200), HttpStatusCode.NotFound);
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsFalse(result.IsUpdateAvailable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Error));
    }

    [TestMethod]
    public async Task ReleaseEndpoint429_ReportsErrorWithoutUpdate()
    {
        var svc = ServiceFor("2.0.0", _ => new HttpResponseMessage((HttpStatusCode)429));
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsFalse(result.IsUpdateAvailable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Error), "Rate limiting is inconclusive, not a silent no-update.");
    }

    [TestMethod]
    public async Task ReleaseEndpoint500_ReportsErrorWithoutUpdate()
    {
        var svc = ServiceFor("2.0.0", _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsFalse(result.IsUpdateAvailable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Error));
    }

    [TestMethod]
    public async Task MalformedReleaseJson_ReportsErrorWithoutUpdate()
    {
        var svc = ServiceFor("2.0.0", _ => TagJson("{not-json"));
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsFalse(result.IsUpdateAvailable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Error));
    }

    [TestMethod]
    public async Task CancelledCheck_ReportsCancellationWithoutUpdate()
    {
        var svc = ServiceFor("2.0.0", _ => TagJson(ReleaseExact200));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await svc.CheckForUpdatesAsync("1.3.0", cts.Token);
        Assert.IsFalse(result.IsUpdateAvailable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Error));
    }
}

[TestClass]
public sealed class UpdateSystem2Tests
{
    [TestMethod]
    public async Task AutomaticCheckTwice_OnlyOneNetworkOperation()
    {
        int calls = 0;
        var session = new UpdateCheckSession("2.0.0", _ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new UpdateCheckResult());
        });
        var first = await session.TryRunAutomaticCheckAsync(true);
        var second = await session.TryRunAutomaticCheckAsync(true);
        Assert.IsNotNull(first);
        Assert.IsNull(second);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task AutomaticCheckDisabled_ZeroOperations()
    {
        bool called = false;
        var session = new UpdateCheckSession("2.0.0", _ =>
        {
            called = true;
            return Task.FromResult(new UpdateCheckResult());
        });
        Assert.IsNull(await session.TryRunAutomaticCheckAsync(false));
        Assert.IsFalse(called);
    }

    [TestMethod]
    public void ShouldPresentUpdateDialog_OnlyRealAvailableResults()
    {
        Assert.IsTrue(UpdateCheckSession.ShouldPresentUpdateDialog(new UpdateCheckResult { IsUpdateAvailable = true, LatestVersion = "2.0.1" }));
        Assert.IsFalse(UpdateCheckSession.ShouldPresentUpdateDialog(null));
        Assert.IsFalse(UpdateCheckSession.ShouldPresentUpdateDialog(new UpdateCheckResult { IsUpdateAvailable = false }));
        Assert.IsFalse(UpdateCheckSession.ShouldPresentUpdateDialog(new UpdateCheckResult { IsUpdateAvailable = true, Error = "x" }));
    }

    [TestMethod]
    public void ShouldDeferUpdateDialog_TrayMinimizedWizardOrRepeat()
    {
        Assert.IsTrue(UpdateCheckSession.ShouldDeferUpdateDialog(false, false, false, false), "Hidden tray window must not pop a modal.");
        Assert.IsTrue(UpdateCheckSession.ShouldDeferUpdateDialog(true, true, false, false), "Minimized window must not pop a modal.");
        Assert.IsTrue(UpdateCheckSession.ShouldDeferUpdateDialog(true, false, true, false), "First-run wizard must never be interrupted.");
        Assert.IsTrue(UpdateCheckSession.ShouldDeferUpdateDialog(true, false, false, true), "Same session must not repeat.");
        Assert.IsFalse(UpdateCheckSession.ShouldDeferUpdateDialog(true, false, false, false), "Visible foreground window presents.");
    }

    [TestMethod]
    public void UpdateDialogTexts_LocalizedPlAndEn()
    {
        var en = UpdateDialogTextProvider.ForLanguage(false);
        var pl = UpdateDialogTextProvider.ForLanguage(true);
        Assert.AreEqual("Update Available", en.Title);
        Assert.AreEqual("Dostępna aktualizacja", pl.Title);
        Assert.AreEqual("Open Release Page", en.OpenRelease);
        Assert.AreEqual("Otwórz stronę wydania", pl.OpenRelease);
        Assert.AreEqual("Later", en.Later);
        Assert.AreEqual("Później", pl.Later);
        Assert.IsTrue(string.Format(en.CurrentVersion, "2.0.0").Contains("2.0.0"));
        Assert.IsTrue(string.Format(pl.CurrentVersion, "2.0.0").Contains("2.0.0"));
        Assert.AreNotEqual(en.Heading, pl.Heading);
    }

    [TestMethod]
    public void ExpectedInstallerAssetName_IsExact()
    {
        Assert.AreEqual("ProcessCoreOptimizer_Setup_2.0.0.exe", UpdateService.ExpectedInstallerAsset("2.0.0"));
        Assert.AreEqual("ProcessCoreOptimizer_Setup_2.0.0.exe", UpdateService.ExpectedInstallerAsset("v2.0.0"));
    }

    [TestMethod]
    public void NoAutomaticDownloadOrExecutionInUpdatePath()
    {
        string root = FindRepoRoot();
        foreach (string rel in new[]
        {
            "ProcessCoreOptimizer/Services/UpdateService.cs",
            "ProcessCoreOptimizer/ViewModels/UpdateCheckSession.cs",
            "ProcessCoreOptimizer/Views/UpdateAvailableDialog.xaml.cs"
        })
        {
            string code = File.ReadAllText(Path.Combine(root, rel));
            foreach (string banned in new[] { "DownloadFile", "DownloadData", "GetByteArray", "WriteAllBytes", "CopyToAsync", ".msi\"", "StartInfo { FileName = \"", "CreateNoWindow" })
            {
                Assert.IsFalse(code.Contains(banned), $"{rel} must not contain '{banned}': updates open the release page only.");
            }
        }
        string service = File.ReadAllText(Path.Combine(root, "ProcessCoreOptimizer/Services/UpdateService.cs"));
        int starts = System.Text.RegularExpressions.Regex.Matches(service, "Process\\.Start").Count;
        Assert.AreEqual(1, starts, "Exactly one Process.Start (the release-page URL opener) is allowed.");
        Assert.IsTrue(service.Contains("UseShellExecute = true"), "URL opening must use the shell, never an executable path.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "version.txt")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "Repo root not found.");
        return dir!.FullName;
    }
}

[TestClass]
public sealed class SessionRestoreTests
{
    [TestMethod]
    public void Capture_IsBounded_NewestKept()
    {
        var svc = new SessionRestoreService();
        for (int i = 0; i < SessionRestoreService.MaxEntries + 5; i++)
        {
            svc.Capture(new ProcessInstanceKey(i + 1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(i)),
                "app", null, 0x1, ProcessPriorityClass.Normal);
        }
        Assert.AreEqual(SessionRestoreService.MaxEntries, svc.Count);
    }

    [TestMethod]
    public void Restore_UnknownProcess_SkipsWithoutMutation()
    {
        var fake = new FakeProcessService();
        var svc = new SessionRestoreService();
        var result = svc.RestoreForProcess("definitely-not-running-xyz", fake, new Dictionary<int, uint>());
        Assert.AreEqual(0, result.Restored);
        Assert.AreEqual(0, result.Failed);
        Assert.AreEqual(0, fake.CoreCalls + fake.PriorityCalls);
    }

    [TestMethod]
    public void Restore_StaleEntry_NeverRestoresIntoRecycledPid()
    {
        using var self = Process.GetCurrentProcess();
        var fake = new FakeProcessService();
        var svc = new SessionRestoreService();
        // Entry claims our PID but a start time from long ago: a recycled PID.
        svc.Capture(
            new ProcessInstanceKey(self.Id, self.StartTime.ToUniversalTime().AddHours(-5)),
            self.ProcessName, null, 0x1, ProcessPriorityClass.Normal);

        var result = svc.RestoreForProcess(self.ProcessName, fake, new Dictionary<int, uint>());
        Assert.AreEqual(0, result.Restored, "A recycled PID must never receive another instance's settings.");
        Assert.AreEqual(0, fake.CoreCalls + fake.PriorityCalls);
    }
}
