using System.Collections.Generic;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// What the OS exposes vs what PCO can address. The classic 64-bit affinity
    /// mask model stays limited to processor group 0 by design; multi-group
    /// systems are detected and reported instead of being silently truncated.
    /// </summary>
    public sealed class CpuTopologyReport
    {
        public int TotalCpuSets { get; set; }
        public int ProcessorGroups { get; set; } = 1;
        public int VisibleLogicalProcessors { get; set; }
        public bool LimitedToGroup0 { get; set; }
        public string LimitationMessage { get; set; } = string.Empty;
    }

    public sealed class CorePreset
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsAvailable { get; set; }
        public string UnavailableReason { get; set; } = string.Empty;
        public List<int> LogicalProcessors { get; set; } = new();
    }

    /// <summary>
    /// Named core selections computed from detected topology. Only presets valid
    /// for the current machine are offered; unknown classifications never produce
    /// P/E-specific presets.
    /// </summary>
    public static class CorePresets
    {
        public static List<CorePreset> Build(IReadOnlyList<CoreInfoDto> cores)
        {
            var result = new List<CorePreset>();
            var all = new List<int>();
            var pCores = new List<int>();
            var eCores = new List<int>();
            var physical = new List<int>();
            var seenPhysical = new HashSet<int>();
            bool anyKnown = false;

            foreach (var c in cores)
            {
                all.Add(c.Index);
                if (!c.IsClassificationUnknown) anyKnown = true;
                if (!c.IsClassificationUnknown && !c.IsECore && !c.IsThread) pCores.Add(c.Index);
                if (!c.IsClassificationUnknown && c.IsECore) eCores.Add(c.Index);
                if (!c.IsClassificationUnknown && !c.IsThread && seenPhysical.Add(c.CoreIndex)) physical.Add(c.Index);
            }

            result.Add(new CorePreset
            {
                Id = "all",
                DisplayName = "All logical processors",
                Description = $"System default: every visible logical processor ({all.Count}).",
                IsAvailable = all.Count > 0,
                LogicalProcessors = all
            });

            result.Add(new CorePreset
            {
                Id = "p-cores",
                DisplayName = "Performance cores",
                Description = pCores.Count > 0 ? $"{pCores.Count} P-core logical processor(s)." : "No performance cores detected.",
                IsAvailable = pCores.Count > 0,
                UnavailableReason = anyKnown ? "No performance cores detected on this CPU." : "Topology classification is Unknown on this system.",
                LogicalProcessors = pCores
            });

            result.Add(new CorePreset
            {
                Id = "e-cores",
                DisplayName = "Efficiency cores",
                Description = eCores.Count > 0 ? $"{eCores.Count} E-core logical processor(s)." : "No efficiency cores detected.",
                IsAvailable = eCores.Count > 0,
                UnavailableReason = anyKnown ? "No efficiency cores detected on this CPU." : "Topology classification is Unknown on this system.",
                LogicalProcessors = eCores
            });

            result.Add(new CorePreset
            {
                Id = "physical",
                DisplayName = "Physical cores without SMT",
                Description = physical.Count > 0 ? $"{physical.Count} physical core(s), first thread only." : "SMT layout unknown.",
                IsAvailable = physical.Count > 0 && physical.Count < all.Count,
                UnavailableReason = physical.Count >= all.Count
                    ? "No SMT threads detected; all cores are already physical."
                    : "SMT layout is Unknown on this system.",
                LogicalProcessors = physical
            });

            return result;
        }
    }

    /// <summary>DTO so presets stay testable without WPF ViewModelBase.</summary>
    public sealed class CoreInfoDto
    {
        public int Index { get; set; }
        public int CoreIndex { get; set; }
        public bool IsThread { get; set; }
        public bool IsECore { get; set; }
        public bool IsClassificationUnknown { get; set; }
    }
}
