Process Core Optimizer 2.0.0 — portable package (no installer)
=============================================================

Requirements: Windows 10 / 11, 64-bit (x64). No .NET runtime needed
(self-contained build). Up to 64 logical processors are used.

How to run:
  1. Extract this ZIP anywhere (e.g. Documents or a USB stick).
  2. Run ProcessCoreOptimizer.exe (double-click, Start Menu pin, or shortcut).
  3. Optional flags: --minimized, --tray, --no-update-check, --safe-mode.

"Portable" means no installer, no registry entries, no services.
User data (settings, profiles, rules, log) is intentionally NOT stored next
to the EXE — it lives in %APPDATA%\ProcessCoreOptimizer, exactly like the
installed version, so profiles survive EXE moves and reinstalls:

  %APPDATA%\ProcessCoreOptimizer\settings.json
  %APPDATA%\ProcessCoreOptimizer\profiles.json
  %APPDATA%\ProcessCoreOptimizer\smartrules.json
  %APPDATA%\ProcessCoreOptimizer\ProcessCoreOptimizer.log

To fully remove the app: delete this folder and %APPDATA%\ProcessCoreOptimizer.

SmartScreen / trust note:
  This build is NOT code-signed (no Authenticode certificate). Windows
  SmartScreen may show a warning on first launch. That is expected for an
  unsigned independent release — verify the SHA-256 in SHA256SUMS.txt, then
  choose "Run anyway" only if you trust the source (github.com/9Erza).

First run: a 3-page wizard explains Affinity vs CPU Sets and startup options.
Nothing is optimized automatically — every change is explicit and logged.

Support the developer: https://buymeacoffee.com/9erza
Sources: https://github.com/9Erza/ProcessCoreOptimizer

---
Process Core Optimizer 2.0.0 — paczka portable (bez instalatora)
=============================================================

Wymagania: Windows 10 / 11, 64-bit (x64). Runtime .NET nie jest potrzebny
(build self-contained). Wykorzystywane do 64 procesorów logicznych.

Uruchomienie:
  1. Wypakuj ZIP w dowolne miejsce.
  2. Uruchom ProcessCoreOptimizer.exe.
  3. Opcjonalne flagi: --minimized, --tray, --no-update-check, --safe-mode.

„Portable" znaczy brak instalatora — Dane użytkownika (ustawienia, profile,
reguły, log) celowo NIE leżą obok EXE, tylko w %APPDATA%\ProcessCoreOptimizer
— tak samo jak w wersji instalacyjnej.

Całkowite usunięcie: skasuj ten folder oraz %APPDATA%\ProcessCoreOptimizer.

SmartScreen / zaufanie:
  Ten build NIE jest podpisany kodem (brak certyfikatu Authenticode). Windows
  SmartScreen może pokazać ostrzeżenie przy pierwszym uruchomieniu. To
  oczekiwane dla niepodpisanego wydania — zweryfikuj SHA-256 z SHA256SUMS.txt
  i wybierz „Uruchom mimo to" tylko, jeśli ufasz źródłu (github.com/9Erza).

Pierwsze uruchomienie: kreator (3 strony) wyjaśnia Affinity vs CPU Sets oraz
autostart. Nic nie jest optymalizowane automatycznie.
