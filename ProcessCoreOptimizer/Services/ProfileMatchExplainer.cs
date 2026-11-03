using ProcessCoreOptimizer.WPF.Models;
using System.Collections.Generic;
using System.Linq;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Human-readable answer to "why this profile?". Pure: same inputs always
    /// produce the same explanation. Mirrors the engine precedence
    /// (path-bound wins, then newest) so the text never contradicts behavior.
    /// </summary>
    public static class ProfileMatchExplainer
    {
        public static string Explain(
            string processName,
            string? executablePath,
            IEnumerable<ProcessProfile> profiles,
            bool polish)
        {
            string name = ProfileService.NormalizeProcessName(processName);
            if (string.IsNullOrWhiteSpace(name))
                return polish ? "Brak procesu." : "No process.";

            var matching = profiles
                .Where(p => p != null && p.IsEnabled && ProfileService.MatchesIdentity(p, name, executablePath))
                .ToList();
            if (matching.Count == 0)
            {
                bool anyDisabled = profiles.Any(p => p != null && !p.IsEnabled
                    && ProfileService.MatchesIdentity(p, name, executablePath));
                return anyDisabled
                    ? (polish ? "Profil pasuje, ale jest wyłączony." : "A profile matches but is disabled.")
                    : (polish ? "Brak pasującego włączonego profilu." : "No enabled profile matches.");
            }

            var winner = ProfileConflicts.OrderByPrecedence(matching).First();
            if (matching.Count == 1)
            {
                return polish
                    ? $"Profil '{winner.DisplayName}': jedyny pasujący włączony profil."
                    : $"Profile '{winner.DisplayName}': the only matching enabled profile.";
            }

            bool winnerPathBound = !string.IsNullOrWhiteSpace(ProfileService.NormalizeExecutablePath(winner.ExecutablePath));
            if (winnerPathBound)
            {
                return polish
                    ? $"Profil '{winner.DisplayName}': path-bound wygrywa z {matching.Count - 1} innymi (ta sama nazwa, inna ścieżka lub name-only)."
                    : $"Profile '{winner.DisplayName}': path-bound wins over {matching.Count - 1} other(s) (same name, different path or name-only).";
            }

            return polish
                ? $"Profil '{winner.DisplayName}': najnowszy z {matching.Count} pasujących (rozstrzygnięcie po UpdatedAt)."
                : $"Profile '{winner.DisplayName}': newest of {matching.Count} matching (UpdatedAt tiebreak).";
        }
    }
}
