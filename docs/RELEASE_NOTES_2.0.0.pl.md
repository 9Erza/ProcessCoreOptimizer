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
- Testy: 72 przypadki MSTest, wszystkie zielone (tożsamość, PID reuse, maski,
  statusy, konflikty, import/eksport, watcher, restore, aktualizator, parzystość lokalizacji).
- CI: restore → Release build → testy → `git diff --check`, zielone.
- Artefakt lokalny: samowystarczalny jednoplikowy `ProcessCoreOptimizer.exe`
  2.0.0 (patrz `docs/INSTALLER.md` — SHA-256 i status instalatora).
- Interaktywne screenshoty: NIE WYKONANO (środowisko headless);
  galeria nadal pokazuje UI 1.3.x i jest tak oznaczona.

## Znane ograniczenia

- Limit UI: grupa 0 / 64 procesory logiczne (projektowo, raportowane w aplikacji).
- Bez sterownika kernel, bez wstrzykiwania kodu, bez omijania anti-cheat;
  procesy chronione są odmawiane z podaniem przyczyny.
- Tożsamość instalatora `Setup.exe` z 1.3.0 nie jest odtwarzalna
  z tego repozytorium (zbudowany zewnętrznie); patrz `docs/INSTALLER.md`.
  Bez zgody opiekuna nie publikujemy wydania.
