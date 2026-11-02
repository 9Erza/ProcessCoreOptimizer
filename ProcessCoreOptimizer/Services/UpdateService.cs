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
        private readonly string _releasesApiUrl = "https://api.github.com/repos/9Erza/ProcessCoreOptimizer/releases/latest";
        private readonly string _releasesUrl = "https://github.com/9Erza/ProcessCoreOptimizer/releases";
        private readonly ILogger _logger = LoggerService.Instance;

        /// <summary>Test seam: when set, clients are created from this factory instead of new HttpClient.</summary>
        public Func<HttpClient>? HttpClientFactory { get; set; }

        private HttpClient CreateClient()
        {
            if (HttpClientFactory != null) return HttpClientFactory();
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ProcessCoreOptimizer");
            return client;
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion)
        {
            try
            {
                string? latestVersionStr;
                using (var client = CreateClient())
                {
                    latestVersionStr = (await client.GetStringAsync($"{_versionRawUrl}?t={Guid.NewGuid()}"))
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

                // version.txt is ahead: only notify when a real published release with a
                // downloadable asset exists. This avoids false positives from a version
                // bump merged before the release is published.
                ReleaseInfo? release = await TryGetLatestReleaseAsync().ConfigureAwait(false);
                if (release == null)
                {
                    return new UpdateCheckResult
                    {
                        LatestVersion = latestVersionStr!,
                        ReleaseUrl = _releasesUrl,
                        Note = "Version file advertises a newer version, but no published release was found. Not notifying."
                    };
                }

                if (!TryParseVersion(release.Tag.TrimStart('v', 'V'), out Version? releaseVersion) || releaseVersion == null)
                {
                    return new UpdateCheckResult
                    {
                        LatestVersion = latestVersionStr!,
                        ReleaseUrl = _releasesUrl,
                        Note = "Latest release tag is not a version. Not notifying."
                    };
                }

                if (releaseVersion <= current)
                {
                    return new UpdateCheckResult
                    {
                        LatestVersion = latestVersionStr!,
                        ReleaseUrl = release.Url,
                        Note = "No published release newer than the current version."
                    };
                }

                if (!release.HasInstallerAsset)
                {
                    return new UpdateCheckResult
                    {
                        LatestVersion = release.Tag,
                        ReleaseUrl = release.Url,
                        Note = "Newer release tag exists but carries no installer asset. Not notifying."
                    };
                }

                return new UpdateCheckResult
                {
                    IsUpdateAvailable = true,
                    LatestVersion = release.Tag.TrimStart('v', 'V'),
                    ReleaseUrl = release.Url
                };
            }
            catch (Exception ex)
            {
                _logger.Debug($"Update check failed: {ex.Message}");
                return new UpdateCheckResult { Error = ex.Message, ReleaseUrl = _releasesUrl };
            }
        }

        private async Task<ReleaseInfo?> TryGetLatestReleaseAsync()
        {
            try
            {
                using var client = CreateClient();
                using var response = await client.GetAsync(_releasesApiUrl).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
                var root = doc.RootElement;

                string tag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? string.Empty : string.Empty;
                string url = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? _releasesUrl : _releasesUrl;
                bool hasAsset = false;

                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                            || name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                            || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            hasAsset = true;
                            break;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(tag)) return null;
                return new ReleaseInfo { Tag = tag, Url = url, HasInstallerAsset = hasAsset };
            }
            catch (Exception ex)
            {
                _logger.Debug($"Release lookup failed: {ex.Message}");
                return null;
            }
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

        private sealed class ReleaseInfo
        {
            public string Tag { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public bool HasInstallerAsset { get; set; }
        }

        public static void OpenReleasePage(string url)
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
    }
}
