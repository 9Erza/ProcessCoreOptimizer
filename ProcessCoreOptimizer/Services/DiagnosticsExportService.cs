using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Builds a PARTIALLY anonymized diagnostics package (ZIP) on explicit user
    /// demand. Anonymization: user-profile path prefixes and embedded
    /// C:\Users\&lt;name&gt; segments become &lt;user&gt;, UNC hosts become
    /// &lt;host&gt;, emails become &lt;email&gt;; free-text notes are excluded;
    /// no machine name / MAC / SID is ever collected. LIMITS (stated openly in
    /// the package header and the export prompt): the user's own rule/profile
    /// NAMES and process names are included as-is; system paths are kept.
    /// </summary>
    public static class DiagnosticsExportService
    {
        private static readonly Regex UserPathRegex = new(
            @"[A-Za-z]:\\Users\\[^\\/:*?""<>|\r\n]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex UncHostRegex = new(
            @"\\\\[^\\/:*?""<>|\r\n\s]+",
            RegexOptions.Compiled);

        private static readonly Regex EmailRegex = new(
            @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}",
            RegexOptions.Compiled);

        public static string AnonymizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "-";
            try
            {
                string userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrWhiteSpace(userDir) && path.StartsWith(userDir, StringComparison.OrdinalIgnoreCase))
                    return "<user>" + path.Substring(userDir.Length);
            }
            catch { }

            return AnonymizeLine(path);
        }

        /// <summary>
        /// Masks identifying fragments anywhere in a line: user profile paths
        /// (incl. embedded), UNC hosts, email addresses. Rule/profile names and
        /// system paths are preserved intentionally (see class remarks).
        /// </summary>
        public static string AnonymizeLine(string? line)
        {
            if (string.IsNullOrEmpty(line)) return line ?? string.Empty;
            string masked = UserPathRegex.Replace(line, m =>
            {
                int usersIdx = m.Value.IndexOf("Users", StringComparison.OrdinalIgnoreCase);
                int sep = m.Value.IndexOf('\\', usersIdx + 5);
                string prefix = sep >= 0 ? m.Value.Substring(0, sep + 1) : m.Value;
                return prefix + "<user>";
            });
            masked = UncHostRegex.Replace(masked, @"\\<host>");
            masked = EmailRegex.Replace(masked, "<email>");
            return masked;
        }

        public static string BuildSummaryText(
            string appVersion,
            IReadOnlyList<Models.ProcessProfile> profiles,
            IReadOnlyList<Models.SmartRule> rules,
            Func<string?, string?> resolveProfileName,
            IReadOnlyList<RuleDecision> recentDecisions,
            string watcherStatus,
            IReadOnlyList<string> logTail)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Process Core Optimizer diagnostics (PARTIALLY anonymized — review before sharing)");
            sb.AppendLine($"Masked: user profile paths (<user>), UNC hosts (<host>), emails (<email>).");
            sb.AppendLine($"NOT masked: your rule/profile/process names, system paths. Notes are excluded.");
            sb.AppendLine($"UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"App: {appVersion}");
            sb.AppendLine($"OS: {Environment.OSVersion.Version} 64-bit={Environment.Is64BitOperatingSystem} CPUs={Environment.ProcessorCount}");
            sb.AppendLine();

            sb.AppendLine($"=== Profiles ({profiles.Count}) ===");
            foreach (var p in profiles.OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"- {p.ProcessName} | path={(p.ExecutablePath == null ? "-" : AnonymizePath(p.ExecutablePath))} | mask=0x{p.AffinityMask:X} | prio={p.Priority} | mode={p.OptimizationMode} | applyP={p.ApplyPriority} applyC={p.ApplyCoreOptimization} eco={(p.EcoQos.HasValue ? p.EcoQos.Value ? "1" : "0" : "-")} | enabled={p.IsEnabled}");
            }

            sb.AppendLine();
            sb.AppendLine($"=== Smart rules ({rules.Count}) ===");
            foreach (var r in rules.OrderBy(r => r.Priority).ThenBy(r => r.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                string target = r.Action == Models.RuleAction.RestoreSession
                    ? "(restore session)"
                    : resolveProfileName(r.ProfileId) ?? "(missing profile)";
                sb.AppendLine($"- {r.Name} | proc={r.ProcessName} | path={(r.ExecutablePath == null ? "-" : AnonymizePath(r.ExecutablePath))} | trigger={r.Trigger} | action={r.Action} -> {target} | delay={r.DelaySeconds}s cooldown={r.CooldownSeconds}s prio={r.Priority} | enabled={r.IsEnabled}");
            }

            sb.AppendLine();
            sb.AppendLine($"=== Recent rule decisions ({recentDecisions.Count}) ===");
            foreach (var d in recentDecisions)
                sb.AppendLine($"- {d.EvaluatedAtUtc:HH:mm:ss} {d.RuleName} -> PID {d.Key.ProcessId} ({d.ProcessName}) [{(d.Ready ? "READY" : "not ready")}]: {d.Reason}");

            sb.AppendLine();
            sb.AppendLine($"=== Watcher ===");
            sb.AppendLine(watcherStatus);

            sb.AppendLine();
            sb.AppendLine($"=== Log tail ({logTail.Count}) ===");
            foreach (var line in logTail)
                sb.AppendLine(AnonymizePath(line));

            return sb.ToString();
        }

        public static void WriteZip(string filePath, string summaryText, IReadOnlyDictionary<string, string>? extraFiles = null)
        {
            using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
            var summary = zip.CreateEntry("diagnostics.txt");
            using (var writer = new StreamWriter(summary.Open(), Encoding.UTF8))
                writer.Write(summaryText);

            if (extraFiles != null)
            {
                foreach (var kv in extraFiles)
                {
                    var entry = zip.CreateEntry(kv.Key);
                    using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                    writer.Write(kv.Value);
                }
            }
        }
    }
}
