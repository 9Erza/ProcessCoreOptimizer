using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Controlled startup profiler, active only with PCO_STARTUP_PROFILE=1.
    /// Zero-cost when disabled (single env check per mark). Writes one log per
    /// process to %TEMP% so repeated launches can be compared. Kept minimal:
    /// timestamps + thread ids, no allocations on the hot path when off.
    /// </summary>
    public static class StartupProfiler
    {
        private static readonly bool Enabled =
            string.Equals(Environment.GetEnvironmentVariable("PCO_STARTUP_PROFILE"), "1", StringComparison.Ordinal);
        private static readonly Stopwatch Watch = Stopwatch.StartNew();
        private static readonly object Gate = new();
        private static readonly List<string> Marks = new();

        public static bool IsEnabled => Enabled;

        public static void Mark(string name)
        {
            if (!Enabled) return;
            lock (Gate)
            {
                Marks.Add($"{Watch.Elapsed.TotalMilliseconds,10:F1}ms  t{Thread.CurrentThread.ManagedThreadId,2}  {name}");
            }
        }

        public static void Dump(string? note = null)
        {
            if (!Enabled) return;
            try
            {
                List<string> snapshot;
                lock (Gate) { snapshot = new List<string>(Marks); }
                string path = Path.Combine(Path.GetTempPath(), $"pco-startup-profile-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
                var lines = new List<string>(snapshot.Count + 2)
                {
                    $"PCO startup profile pid={Environment.ProcessId} note={note ?? "-"}"
                };
                lines.AddRange(snapshot);
                File.WriteAllLines(path, lines);
            }
            catch { }
        }
    }
}
