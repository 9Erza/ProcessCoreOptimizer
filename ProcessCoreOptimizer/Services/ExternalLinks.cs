using ProcessCoreOptimizer.WPF.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ProcessCoreOptimizer.WPF.Services
{
    public enum PcoExternalLink
    {
        Author,
        Repository,
        Support
    }

    /// <summary>
    /// Central gate for the three official outbound links. Only exact allowlisted
    /// HTTPS URLs can ever be opened; nothing is built from user data, nothing is
    /// tracked, no network requests are made by the app itself.
    /// </summary>
    public static class ExternalLinks
    {
        public const string AuthorUrl = "https://github.com/9Erza";
        public const string RepositoryUrl = "https://github.com/9Erza/ProcessCoreOptimizer";
        public const string SupportUrl = "https://buymeacoffee.com/9erza";

        private static readonly IReadOnlyDictionary<PcoExternalLink, string> OwnedLinks =
            new Dictionary<PcoExternalLink, string>
            {
                [PcoExternalLink.Author] = AuthorUrl,
                [PcoExternalLink.Repository] = RepositoryUrl,
                [PcoExternalLink.Support] = SupportUrl
            };

        /// <summary>Test seam: receives the start info, returns process-started.</summary>
        public static Func<ProcessStartInfo, bool> Launcher { get; set; } = DefaultLauncher;

        public static string GetUrl(PcoExternalLink link) => OwnedLinks[link];

        public static bool IsAllowed(string? url) =>
            !string.IsNullOrWhiteSpace(url) &&
            (string.Equals(url.Trim(), AuthorUrl, StringComparison.Ordinal)
                || string.Equals(url.Trim(), RepositoryUrl, StringComparison.Ordinal)
                || string.Equals(url.Trim(), SupportUrl, StringComparison.Ordinal));

        public static bool TryOpen(PcoExternalLink link)
        {
            try
            {
                return Launcher(new ProcessStartInfo
                {
                    FileName = OwnedLinks[link],
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Debug($"Could not open external link {link}: {ex.Message}");
                return false;
            }
        }

        /// <summary>Opens only exact allowlisted URLs; anything else is refused.</summary>
        public static bool TryOpenUrl(string? url)
        {
            if (!IsAllowed(url)) return false;
            try
            {
                return Launcher(new ProcessStartInfo
                {
                    FileName = url!.Trim(),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Debug($"Could not open external URL: {ex.Message}");
                return false;
            }
        }

        private static bool DefaultLauncher(ProcessStartInfo psi)
        {
            using var proc = Process.Start(psi);
            return proc != null;
        }
    }
}
