namespace ProcessCoreOptimizer.WPF.ViewModels
{
    /// <summary>All user-facing strings of the update dialog, resolved once per open.</summary>
    public sealed record UpdateDialogTexts(
        string Title,
        string Heading,
        string CurrentVersion,
        string OpenRelease,
        string Later);

    public static class UpdateDialogTextProvider
    {
        public static UpdateDialogTexts ForLanguage(bool isPolish) => isPolish
            ? new UpdateDialogTexts(
                Title: "Dostępna aktualizacja",
                Heading: "Dostępna jest nowa wersja Process Core Optimizer.",
                CurrentVersion: "Zainstalowano: v{0}",
                OpenRelease: "Otwórz stronę wydania",
                Later: "Później")
            : new UpdateDialogTexts(
                Title: "Update Available",
                Heading: "A new version of Process Core Optimizer is available.",
                CurrentVersion: "Installed: v{0}",
                OpenRelease: "Open Release Page",
                Later: "Later");
    }
}
