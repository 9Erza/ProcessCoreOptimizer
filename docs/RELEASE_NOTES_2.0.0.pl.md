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
aktualizator świadomy wydań (bez fałszywych powiadomień przed publikacją),
rejestracja autostartu tylko przy zmianie ustawień startu oraz zwijany
Developer Hub w sidebarze (karta wersji + oficjalne linki + nawigacja
do About w aplikacji, pełne PL/EN).

## Zgodność

- Profile i ustawienia z 1.3.x wczytują się bez zmian (schemat v2, legacy
  `Exclusive` migruje do `Affinity`).
- Uszkodzony JSON cofa się do `.bak` dla profili i ustawień.
- Źródło wersji: `version.txt` → MSBuild → metadane PE
  (`2.0.0.0` / `2.0.0+sha`), UI, aktualizator, dokumentacja.

## Weryfikacja

- Release build: 0 ostrzeżeń, 0 błędów.
- Testy: 240 przypadków MSTest — 237 zielonych, 3 integracyjne testy prawdziwego
  startupu pominięte bez `PCO_REAL_STARTUP_TESTS=1`
  (fałszywy backend, read-only, negatywne testy tożsamości P0, macierz restore/
  suspension CPU Sets, planner/koordynator/backend startupu, bezpieczeństwo
  resetu, onboarding, protokół helpera, bramki aktualizatora (w tym strażniki
  no-downgrade-prompt i nieparsowalnego tagu), parzystość lokalizacji,
  strażnik trybu wiązań XAML Run, strażniki stopki Developer Hub oraz prawdziwe
  Windows API na izolowanych własnych kopiach testowych w `%TEMP%`). Ręcznie
  włączone lokalnie: 3 prawdziwe testy startupu zielone, zero pozostałości
  w rejestrze/harmonogramie. Żaden proces użytkownika, usługi ani agenta nie
  jest dotykany.
- Architektura startup: plan → wykonaj → zweryfikuj z kolejnością
  create-before-delete; ścieżki anulowania UAC i nieudanego tworzenia
  udowodnione testami.
- GUI: 21/21 kontroli UI Automation na Windows 11 (1920×1080, 100% DPI):
  siedem widoków (w tym Smart Rules) prawdziwymi kliknięciami, przełącznik
  widoku instancji, maximize/minimize/restore, close świadomy tray, tekst
  statusu startupu, przewinięta sekcja startup, przełącznik PL ze screenshotami,
  linia safe-mode, linki about z nazwami automatyzacji, strony kreatora + Finish
  + stempel ustawień, izolowany smoke-test portable przy pierwszym starcie,
  Developer Hub zwinięty/rozwinięty EN+PL z kontrolą wierszy, przełączanie
  spacją (klawiatura), nawigacja do About w aplikacji, kontrola przepełnienia
  przy minimalnym rozmiarze 1000×700.
  Harness: `tools/Run-GuiVerification.ps1` (backup i restore `%APPDATA%`,
  start z `--safe-mode`).
- Defekty GUI wykryte na żywo i naprawione: crash zakresu theme przy starcie
  (teraz poziom App), wersja z `+sha`, ucięte nazwy procesów, ucięte logi,
  przepełnienie przycisków profili, prawa uchwytów CPU Sets (wykryły to prawdziwe testy),
  modal kreatora niewidoczny dla drzewa UIA (fallback Win32), sekwencjonowanie
  minimize/restore vs tray w harnessach, wiązania `Run.Text` TwoWay rzucające
  na źródłach read-only i zabijające panel szczegółów (teraz jawne `Mode=OneWay`
  egzekwowane przez `XamlBindingModeTests`; dowód:
  `docs/screenshots/screen_processes_details.png`), za wąskie kolumny IN/RAM/CPU
  (poszerzone po audycie screenshotów).
- Baseline wydajności (Win11, i5-10210U, 8 LP, 100% DPI, opublikowany Release):
  start 1,5–1,6 s; idle 60 s ≈ 190 MB WS / 90 MB Private / 0,55% CPU /
  ~786 uchwytów / 23 wątki; 100 syntetycznych profili ≈ brak delty CPU, +2 MB WS;
  monitor HW ON ≈ +14 MB WS, +60 uchwytów; zminimalizowany ≈ 0,27% CPU.
  Harness: `tools/Measure-Performance.ps1`, `tools/Measure-Cpu.ps1`;
  surowe CSV w ignorowanym `.evidence/perf`.
- CI: restore → Release build → testy → `git diff --check`, zielone.
- Artefakty lokalne: samowystarczalny jednoplikowy `ProcessCoreOptimizer.exe` 2.0.0
  oraz `installer/Output/ProcessCoreOptimizer_Setup_2.0.0.exe` (finalny build
  gałęzi: 54 052 174 bajtów, SHA-256
  `D5AFFF5D34680EEEF51ED1EAAF823B273A98A8710C7E211AA4030502A0024CD8`,
  PE 2.0.0; odtwarzalny przez `tools/Build-Installer.ps1`) — patrz
  `docs/INSTALLER.md` i `tools/Validate-Release.ps1` (8/8 kontroli zielonych).
- Interaktywne screenshoty: prawdziwe UI 2.0 (7 widoków + przełącznik instancji
  + strony kreatora + smoke-test portable + zwijany Developer Hub EN/PL);
  bez mockupów. Galeria README: `ProcessCoreOptimizer/Screenshots/` (6 ujęć);
  pełny przebieg: ignorowany `.evidence/gui/` (26 ujęć).
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
