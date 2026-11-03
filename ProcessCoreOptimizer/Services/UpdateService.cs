using ProcessCoreOptimizer.WPF.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ProcessCoreOptimizer.WPF.Services
{
    public sealed class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; set; }
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public string? Error { get; set; }
        /// <summary>Human-readable reason, e.g. version file ahead of any published release.</summary>
        public string? Note { get; set; }
    }

    public sealed class UpdateService
    {
        private readonly string _versionRawUrl = "https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/refs/heads/main/version.txt";
        private readonly string _releasesUrl = "https://github.com/9Erza/ProcessCoreOptimizer/releases";
        private const string ReleasesTagUrlTemplate = "https://api.github.com/repos/9Erza/ProcessCoreOptimizer/releases/tags/{0}";
        private readonly ILogger _logger = LoggerService.Instance;

        /// <summary>Test seam: when set, clients are created from this factory instead of new HttpClient.</summary>
        public Func<HttpClient>? HttpClientFactory { get; set; }

        /// <summary>Exact installer asset for a version (no substitutes accepted).</summary>
        public static string ExpectedInstallerAsset(string version) =>
            $"ProcessCoreOptimizer_Setup_{version.Trim().TrimStart('v', 'V')}.exe";

        private HttpClient CreateClient()
        {
            if (HttpClientFactory != null) return HttpClientFactory();
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ProcessCoreOptimizer");
            return client;
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion)
        {
            return await CheckForUpdatesAsync(currentVersion, System.Threading.CancellationToken.None).ConfigureAwait(false);
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                string? latestVersionStr;
                using (var client = CreateClient())
                {
                    using var cts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(8));
                    latestVersionStr = (await client.GetStringAsync($"{_versionRawUrl}?t={Guid.NewGuid()}", cts.Token).ConfigureAwait(false))
                        .Trim()
                        .TrimStart('v', 'V');
                }

                if (!TryParseVersion(latestVersionStr, out Version? latest) || latest == null)
                {
                    return new UpdateCheckResult { Error = "Unrecognized remote version.", ReleaseUrl = _releasesUrl };
                }

                if (!TryParseVersion(currentVersion.TrimStart('v', 'V'), out Version? current) || current == null)
                {
                    return new UpdateCheckResult { Error = "Unrecognized local version.", ReleaseUrl = _releasesUrl };
                }

                if (latest <= current)
                {
                    return new UpdateCheckResult { LatestVersion = latestVersionStr!, ReleaseUrl = _releasesUrl };
                }

                // version.txt is ahead: notify ONLY when a real published (non-draft,
                // non-prerelease) release exists for the matching tag AND carries the
                // exact installer asset. A definitive "not ready" suppresses silently;
                // an inconclusive network answer surfaces as Error (automatic checks
                // stay silent, manual checks report the failure honestly).
                bool? published = await IsReleasePublishedAsync(latestVersionStr!, cancellationToken).ConfigureAwait(false);
                if (published == true)
                {
                    return new UpdateCheckResult
                    {
                        IsUpdateAvailable = true,
                        LatestVersion = latestVersionStr!,
                        ReleaseUrl = $"https://github.com/9Erza/ProcessCoreOptimizer/releases/tag/v{latestVersionStr}"
                    };
                }

                if (published == false)
                {
                    return new UpdateCheckResult
                    {
                        LatestVersion = latestVersionStr!,
                        ReleaseUrl = _releasesUrl,
                        Note = "Version file advertises a newer version, but no published release with the exact installer was found. Not notifying."
                    };
                }

                return new UpdateCheckResult
                {
                    LatestVersion = latestVersionStr!,
                    ReleaseUrl = _releasesUrl,
                    Error = "Could not verify the published release."
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.Debug("Update check cancelled.");
                return new UpdateCheckResult { Error = "Update check cancelled.", ReleaseUrl = _releasesUrl };
            }
            catch (Exception ex)
            {
                _logger.Debug($"Update check failed: {ex.Message}");
                return new UpdateCheckResult { Error = ex.Message, ReleaseUrl = _releasesUrl };
            }
        }

        /// <returns>
        /// True when a published (non-draft, non-prerelease) release exists for the
        /// version tag, its tag matches, and it carries the exact installer asset
        /// (<c>ProcessCoreOptimizer_Setup-X.Y.Z.exe</c>). False when it definitively
        /// does not (missing tag, draft, prerelease, tag mismatch, or no exact asset —
        /// a release without the installer is not a ready update). Null when the
        /// network answer was inconclusive (HTTP error, malformed JSON, timeout).
        /// </returns>
        private async Task<bool?> IsReleasePublishedAsync(string version, System.Threading.CancellationToken cancellationToken)
        {
            string expectedAsset = ExpectedInstallerAsset(version);
            foreach (string tag in new[] { "v" + version, version })
            {
                HttpResponseMessage response;
                try
                {
                    using var client = CreateClient();
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        string.Format(ReleasesTagUrlTemplate, tag));
                    if (!request.Headers.Contains("User-Agent"))
                    {
                        request.Headers.UserAgent.ParseAdd("ProcessCoreOptimizer");
                    }
                    request.Headers.Accept.ParseAdd("application/vnd.github+json");
                    using var cts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(8));
                    response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Release verification failed: {ex.Message}");
                    return null;
                }

                using (response)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.Debug($"Release verification returned {(int)response.StatusCode}.");
                        return null;
                    }

                    try
                    {
                        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
                        JsonElement root = doc.RootElement;

                        string? tagName = root.TryGetProperty("tag_name", out JsonElement tagElement) &&
                            tagElement.ValueKind == JsonValueKind.String
                            ? tagElement.GetString()
                            : null;
                        if (!string.Equals(tagName, "v" + version, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(tagName, version, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.Debug($"Release tag mismatch: expected {tag}, got {tagName ?? "<none>"}.");
                            return false;
                        }

                        if (root.TryGetProperty("draft", out JsonElement draft) &&
                            draft.ValueKind == JsonValueKind.True)
                        {
                            return false;
                        }
                        if (root.TryGetProperty("prerelease", out JsonElement prerelease) &&
                            prerelease.ValueKind == JsonValueKind.True)
                        {
                            return false;
                        }

                        if (root.TryGetProperty("assets", out JsonElement assets) &&
                            assets.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement asset in assets.EnumerateArray())
                            {
                                if (asset.ValueKind == JsonValueKind.Object &&
                                    asset.TryGetProperty("name", out JsonElement name) &&
                                    name.ValueKind == JsonValueKind.String &&
                                    string.Equals(name.GetString(), expectedAsset, StringComparison.OrdinalIgnoreCase))
                                {
                                    return true;
                                }
                            }
                        }

                        _logger.Debug($"Release {tagName} carries no {expectedAsset}; not a ready update.");
                        return false;
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Release verification parse failed: {ex.Message}");
                        return null;
                    }
                }
            }

            return false;
        }

        private static bool TryParseVersion(string? text, out Version? version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            try
            {
                version = Version.Parse(text.Trim());
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void OpenReleasePage(string url)
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
    }
}
