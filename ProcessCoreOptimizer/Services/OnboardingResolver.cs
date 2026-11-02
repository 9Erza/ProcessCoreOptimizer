namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Pure first-run decision table: fresh installs (and post-wipe launches) show
    /// the wizard, legacy data upgrades silently, interrupted runs resume.
    /// Current onboarding version is 1.
    /// </summary>
    public static class OnboardingResolver
    {
        public const int CurrentVersion = 1;

        public static (bool NeedsWizard, int? NewMarker, bool Save) Resolve(bool hadSettings, bool hadProfiles, int marker)
        {
            if (!hadSettings && !hadProfiles)
            {
                if (marker < 0) return (true, 0, true);
                return (marker < CurrentVersion, null, false);
            }

            if (marker < 0) return (false, CurrentVersion, true);
            return (marker < CurrentVersion, null, false);
        }
    }
}
