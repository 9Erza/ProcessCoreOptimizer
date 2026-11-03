using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
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

    private static UpdateService ServiceFor(string versionTxt, HttpStatusCode releaseStatus, string releaseJson)
    {
        var svc = new UpdateService();
        svc.HttpClientFactory = () => new HttpClient(new FakeHandler(req =>
        {
            string url = req.RequestUri?.ToString() ?? string.Empty;
            if (url.Contains("version.txt"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(versionTxt, Encoding.UTF8) };
            if (url.Contains("releases/latest"))
            {
                var resp = new HttpResponseMessage(releaseStatus);
                if (releaseStatus == HttpStatusCode.OK)
                    resp.Content = new StringContent(releaseJson, Encoding.UTF8, "application/json");
                return resp;
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        return svc;
    }

    private const string ReleaseWithExe = """{"tag_name":"v2.0.0","html_url":"https://example/r","assets":[{"name":"ProcessCoreOptimizer_Setup.exe"}]}""";
    private const string ReleaseNoAsset = """{"tag_name":"v2.0.0","html_url":"https://example/r","assets":[]}""";
    private const string ReleaseOld = """{"tag_name":"v1.3.0","html_url":"https://example/r","assets":[{"name":"ProcessCoreOptimizer_Setup.exe"}]}""";

    [TestMethod]
    public async Task NewerVersionWithAsset_Notifies()
    {
        var svc = ServiceFor("2.0.0", HttpStatusCode.OK, ReleaseWithExe);
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsTrue(result.IsUpdateAvailable);
        Assert.AreEqual("2.0.0", result.LatestVersion);
    }

    [TestMethod]
    public async Task VersionAheadButNoPublishedRelease_DoesNotNotify()
    {
        var svc = ServiceFor("2.0.0", HttpStatusCode.NotFound, string.Empty);
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsFalse(result.IsUpdateAvailable, "No false positive before the release is published.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Note));
    }

    [TestMethod]
    public async Task ReleaseWithoutInstallerAsset_DoesNotNotify()
    {
        var svc = ServiceFor("2.0.0", HttpStatusCode.OK, ReleaseNoAsset);
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsFalse(result.IsUpdateAvailable);
    }

    [TestMethod]
    public async Task SameVersion_DoesNotNotify()
    {
        var svc = ServiceFor("1.3.0", HttpStatusCode.OK, ReleaseOld);
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsFalse(result.IsUpdateAvailable);
    }

    [TestMethod]
    public async Task PublishedReleaseOlderThanCurrent_NoDowngradePrompt()
    {
        var svc = ServiceFor("2.1.0", HttpStatusCode.OK, ReleaseOld);
        var result = await svc.CheckForUpdatesAsync("2.0.0");
        Assert.IsFalse(result.IsUpdateAvailable, "An older published release must never prompt a downgrade.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Note));
    }

    [TestMethod]
    public async Task UnparsableReleaseTag_DoesNotNotify()
    {
        const string weird = """{"tag_name":"nightly-xyz","html_url":"https://example/r","assets":[{"name":"a.zip"}]}""";
        var svc = ServiceFor("2.0.0", HttpStatusCode.OK, weird);
        var result = await svc.CheckForUpdatesAsync("1.3.0");
        Assert.IsFalse(result.IsUpdateAvailable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Note));
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
