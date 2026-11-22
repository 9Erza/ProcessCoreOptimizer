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
        public static List<CorePreset> Build(IReadOnlyList<CoreInfoDto> cores, bool polish = false)
        {
            string T(string en, string pl) => polish ? pl : en;
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
                DisplayName = T("All logical processors", "Wszystkie procesory logiczne"),
                Description = T($"System default: every visible logical processor ({all.Count}).", $"Domyślne ustawienie systemu: wszystkie widoczne procesory logiczne ({all.Count})."),
                IsAvailable = all.Count > 0,
                LogicalProcessors = all
            });

            result.Add(new CorePreset
            {
                Id = "p-cores",
                DisplayName = T("Performance cores", "Rdzenie wydajnościowe (P)"),
                Description = pCores.Count > 0 ? T($"{pCores.Count} P-core logical processor(s).", $"Procesory logiczne rdzeni P: {pCores.Count}.") : T("No performance cores detected.", "Nie wykryto rdzeni wydajnościowych."),
                IsAvailable = pCores.Count > 0,
                UnavailableReason = anyKnown ? T("No performance cores detected on this CPU.", "Ten procesor nie ma rdzeni wydajnościowych.") : T("Topology classification is Unknown on this system.", "Klasyfikacja topologii jest nieznana w tym systemie."),
                LogicalProcessors = pCores
            });

            result.Add(new CorePreset
            {
                Id = "e-cores",
                DisplayName = T("Efficiency cores", "Rdzenie energooszczędne (E)"),
                Description = eCores.Count > 0 ? T($"{eCores.Count} E-core logical processor(s).", $"Procesory logiczne rdzeni E: {eCores.Count}.") : T("No efficiency cores detected.", "Nie wykryto rdzeni energooszczędnych."),
                IsAvailable = eCores.Count > 0,
                UnavailableReason = anyKnown ? T("No efficiency cores detected on this CPU.", "Ten procesor nie ma rdzeni energooszczędnych.") : T("Topology classification is Unknown on this system.", "Klasyfikacja topologii jest nieznana w tym systemie."),
                LogicalProcessors = eCores
            });

            result.Add(new CorePreset
            {
                Id = "physical",
                DisplayName = T("Physical cores without SMT", "Rdzenie fizyczne bez SMT"),
                Description = physical.Count > 0 ? T($"{physical.Count} physical core(s), first thread only.", $"Rdzenie fizyczne: {physical.Count}, tylko pierwszy wątek.") : T("SMT layout unknown.", "Układ SMT nieznany."),
                IsAvailable = physical.Count > 0 && physical.Count < all.Count,
                UnavailableReason = physical.Count >= all.Count
                    ? T("No SMT threads detected; all cores are already physical.", "Nie wykryto wątków SMT; wszystkie rdzenie są już fizyczne.")
                    : T("SMT layout is Unknown on this system.", "Układ SMT jest nieznany w tym systemie."),
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
