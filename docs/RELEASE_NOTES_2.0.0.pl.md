# Process Core Optimizer 2.0.0 — Informacja o wydaniu

> Status: **gotowe do przeglądu przez opiekuna, NIE opublikowane.**
> Nie utworzono publicznego GitHub Release z tej gałęzi.

## Co nowego

**Silnik 2.0** — każda mutacja procesu przechodzi bramkę tożsamości
(PID + czas startu + nazwa + ścieżka EXE, gdy profil ją wiąże), weryfikowaną
na świeżym uchwycie OS tuż przed zapisem. Niekontrolowane ścieżki PID-only
usunięto. Wyniki mają jawny model (`FullyApplied`, `PartiallyApplied`,
`Failed`, pominięcia) — częściowy sukces jest uczciwie raportowany
i ponawiany, nigdy cachowany jako wykonany. Affinity waliduje maski z góry,
a nieudane czyszczenie CPU Sets jest widoczne zamiast ukrytego.

**Profile 2.0** — deterministyczne wykrywanie konfliktów z pierwszeństwem
path-bound, duplikowanie / import / eksport (walidowany JSON), szkice
z bieżących ustawień, historia operacji oraz wydzielony serwis watchera
z ochroną przed nakładaniem się, anulowaniem i zamykaniem.

**CPU i topologia** — uczciwy stan Unknown przy braku danych z firmware,
nazwane presety rdzeni (All / P-cores / E-cores / physical-without-SMT,
tylko gdy poprawne dla maszyny), wykrywanie wielu grup z jawnym limitem
grupy 0, ścieżki EXE w snapshotach, szczegóły procesów (PID, czas startu,
ścieżka, źródło affinity, ostatni status) oraz filtry procesów i profili.

**Pulpit 2.0** — tokeny projektu (`Themes/`), sześć widoków
(Processes, CPU & Topology, Profiles, Hardware, Settings, Diagnostics),
przywracanie ustawień sprzed optymalizacji (tylko zweryfikowana tożsamość),
aktualizator świadomy wydań (bez fałszywych powiadomień przed publikacją)
oraz rejestracja autostartu tylko przy zmianie ustawień startu.

## Zgodność

- Profile i ustawienia z 1.3.x wczytują się bez zmian (schemat v2, legacy
  `Exclusive` migruje do `Affinity`).
- Uszkodzony JSON cofa się do `.bak` dla profili i ustawień.
- Źródło wersji: `version.txt` → MSBuild → metadane PE
  (`2.0.0.0` / `2.0.0+sha`), UI, aktualizator, dokumentacja.

## Weryfikacja

- Release build: 0 ostrzeżeń, 0 błędów.
- Testy: 97 przypadków MSTest, wszystkie zielone —
  logika na fake backend, read-only self-PID, negatywne testy tożsamości P0,
  macierz restore/suspension CPU Sets, bramki aktualizatora, parzystość lokalizacji
  oraz prawdziwe Windows API na izolowanych własnych kopiach testowych w `%TEMP%`
  (round-tripy affinity/CPU Sets/priorytetu z prawdziwymi kodami Win32, odmowa
  dla zakończonego procesu, watcher + restore + suspension, path-bound mismatch).
  Żaden proces użytkownika, usługi ani agenta nie jest dotykany.
- GUI: 11/11 kontroli UI Automation na Windows 11 (sesja 1, 1920×1080, 100% DPI):
  sześć zakładek prawdziwymi kliknięciami, maximize/minimize/restore,
  tray-intercept close, linia safe-mode. Harness: `tools/Run-GuiVerification.ps1`
  (backup i restore `%APPDATA%`, start z `--safe-mode`).
- Defekty GUI wykryte na żywo i naprawione: crash zakresu theme przy starcie
  (teraz poziom App), wersja z `+sha`, ucięte nazwy procesów, ucięte logi,
  przepełnienie przycisków profili, prawa uchwytów CPU Sets (wykryły to prawdziwe testy).
- Baseline wydajności (Win11, i5-10210U, 8 LP, 100% DPI): start 1,5–1,6 s;
  idle 60 s ≈ 180 MB WS / 85 MB Private / 0,5% CPU / ~740 uchwytów / 21 wątków;
  100 syntetycznych profili ≈ +5 MB WS, bez delty CPU; monitor HW ON ≈ +10 MB WS,
  +80 uchwytów; zminimalizowany ≈ idle. Harness: `tools/Measure-Performance.ps1`,
  `tools/Measure-Cpu.ps1`; surowe CSV w ignorowanym `.evidence/perf`.
- CI: restore → Release build → testy → `git diff --check`, zielone.
- Artefakty lokalne: samowystarczalny jednoplikowy `ProcessCoreOptimizer.exe` 2.0.0
  oraz `installer/Output/ProcessCoreOptimizer_Setup_2.0.0.exe` (50 784 844 bajtów,
  SHA-256 `11DE86C0…D6795D9F1`, PE 2.0.0) — patrz `docs/INSTALLER.md`.
- Interaktywne screenshoty: prawdziwe UI 2.0 (6 ekranów) opublikowane; bez mockupów.
- Instalacja/upgrade/uninstall na VM: NIE WYKONANO (brak izolowanej VM; instalacja
  na maszynie deweloperskiej celowo uniknięta). Sprzęt multi-group: NIE WYKONANO
  (maszyna 8-LP); przetestowano syntetyczną dyscyplinę mapowania.

## Znane ograniczenia

- Limit UI: grupa 0 / 64 procesory logiczne (projektowo, raportowane w aplikacji).
- Bez sterownika kernel, bez wstrzykiwania kodu, bez omijania anti-cheat;
  procesy chronione są odmawiane z podaniem przyczyny.
- Tożsamość instalatora `Setup.exe` z 1.3.0 nie jest odtwarzalna
  z tego repozytorium (zbudowany zewnętrznie); patrz `docs/INSTALLER.md`.
  Bez zgody opiekuna nie publikujemy wydania.
