using ProcessCoreOptimizer.WPF.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Read-only preview: compares what a profile wants against the live state
    /// of a process, and captures live settings into a profile draft.
    /// Never mutates anything.
    /// </summary>
    public sealed class ProfilePreviewRow
    {
        public string Action { get; set; } = string.Empty;
        public string Requested { get; set; } = string.Empty;
        public string Current { get; set; } = string.Empty;
        public bool Matches { get; set; }
        public string Detail { get; set; } = string.Empty;
    }

    public sealed class ProfilePreview
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public List<ProfilePreviewRow> Rows { get; set; } = new();
        public bool AllMatch { get; set; }
        public string Summary { get; set; } = string.Empty;
    }

    public static class ProfilePreviewService
    {
        public static ProfilePreview Compare(ProcessProfile profile, int pid, Dictionary<int, uint> cpuSetMap, ProcessService processService, bool allowRealtimePriority)
        {
            var preview = new ProfilePreview { ProcessId = pid };
            try
            {
                using var proc = Process.GetProcessById(pid);
                preview.ProcessName = proc.ProcessName;
            }
            catch
            {
                preview.ProcessName = profile.ProcessName;
            }

            string priority = PriorityService.Normalize(profile?.Priority, allowRealtimePriority);

            if (profile?.ApplyCoreOptimization == true)
            {
                var selection = processService.GetCurrentCoreSelection(pid, cpuSetMap);
                bool matches = selection.Success && selection.Mask == profile.AffinityMask;
                preview.Rows.Add(new ProfilePreviewRow
                {
                    Action = "Cores",
                    Requested = FormatMask(profile.AffinityMask, cpuSetMap),
                    Current = selection.Success ? FormatMask(selection.Mask, cpuSetMap) : "unreadable",
                    Matches = matches,
                    Detail = selection.Success ? $"source={selection.Source}" : selection.Message
                });
            }

            if (profile?.ApplyPriority == true)
            {
                var current = processService.TryGetPriority(pid);
                bool matches = current.HasValue && string.Equals(current.Value.ToString(), priority, StringComparison.OrdinalIgnoreCase);
                preview.Rows.Add(new ProfilePreviewRow
                {
                    Action = "Priority",
                    Requested = priority,
                    Current = current?.ToString() ?? "unreadable",
                    Matches = matches,
                    Detail = current.HasValue ? string.Empty : "OS refused the read (elevation or protected process)."
                });
            }

            if (profile?.EcoQos.HasValue == true)
            {
                var eco = processService.TryGetEcoQos(pid);
                string requested = profile.EcoQos.Value ? "EcoQoS on" : "EcoQoS off";
                string current = !eco.Readable || !eco.Enabled.HasValue
                    ? "unreadable"
                    : eco.Enabled.Value ? "EcoQoS on" : "EcoQoS off";
                preview.Rows.Add(new ProfilePreviewRow
                {
                    Action = "EcoQoS",
                    Requested = requested,
                    Current = current,
                    Matches = eco.Readable && eco.Enabled.HasValue && eco.Enabled.Value == profile.EcoQos.Value,
                    Detail = eco.Readable ? (eco.Error ?? string.Empty) : (eco.Error ?? "OS refused the read (elevation, protected process, or unsupported OS).")
                });
            }

            preview.AllMatch = preview.Rows.Count > 0 && preview.Rows.TrueForAll(r => r.Matches);
            preview.Summary = preview.Rows.Count == 0
                ? "Profile requests no actions."
                : preview.AllMatch
                    ? "Live settings already match this profile."
                    : "Live settings differ from this profile.";
            return preview;
        }

        /// <summary>
        /// Captures the current affinity + priority of a live process into an
        /// unsaved profile draft. Returns null when the OS refuses the reads.
        /// </summary>
        public static ProcessProfile? CaptureFromLive(int pid, Dictionary<int, uint> cpuSetMap, ProcessService processService)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                if (proc.HasExited) return null;
                string name = proc.ProcessName;

                var selection = processService.GetCurrentCoreSelection(pid, cpuSetMap);
                var priority = processService.TryGetPriority(pid);
                if (!selection.Success || selection.Mask == 0 || !priority.HasValue) return null;

                bool? ecoQos = null;
                try
                {
                    var eco = processService.TryGetEcoQos(pid);
                    if (eco.Readable) ecoQos = eco.Enabled;
                }
                catch { ecoQos = null; }

#pragma warning disable CS0618
                var mode = selection.Mode == OptimizationMode.Exclusive ? OptimizationMode.Affinity : selection.Mode;
#pragma warning restore CS0618

                return new ProcessProfile
                {
                    ProcessName = name,
                    AffinityMask = selection.Mask,
                    Priority = priority.Value.ToString(),
                    OptimizationMode = mode,
                    ApplyPriority = true,
                    ApplyCoreOptimization = true,
                    EcoQos = ecoQos,
                    IsEnabled = false,
                    Notes = $"Captured from live PID {pid}."
                };
            }
            catch
            {
                return null;
            }
        }

        public static string FormatMask(long mask, Dictionary<int, uint> cpuSetMap)
        {
            int bits = 0;
            for (int i = 0; i < 64; i++) if ((mask & (1L << i)) != 0) bits++;
            return $"0x{mask:X} ({bits} logical processor{(bits == 1 ? string.Empty : "s")})";
        }
    }
}
