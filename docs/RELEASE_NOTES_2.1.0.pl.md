# Process Core Optimizer 2.1.0 — notatki wydania

> Status: **opublikowane 2026-11-22 jako stabilne wydanie GitHub v2.1.0.**

## Co nowego

**Interfejs 2.1** — pełna przebudowa wyglądu na minimalistyczny ciemny
interfejs: boczna nawigacja ikonami ze wskaźnikiem aktywnej zakładki, nagłówki
stron, karty, puste stany, jeden kolor akcentu, przełączniki i filtry-chipy.
Przebudowano wszystkie widoki (Procesy, CPU i topologia, Profile, Smart Rules,
Sprzęt, Diagnostyka, Ustawienia) oraz okna dialogowe.

**Natywne okno** — główne okno używa WindowChrome zamiast renderowanego
programowo okna warstwowego: kompozycja GPU, Aero Snap, Snap Layouts z Windows 11,
ciemna ramka DWM i zaokrąglone rogi.

**Autostart** — wybór z kreatora pierwszego uruchomienia jest rejestrowany w
Windows; brakujący lub nieaktualny wpis jest naprawiany przy starcie; flaga
„wyłączone” z Menedżera zadań → Aplikacje autostartu jest wykrywana i czyszczona;
w Ustawieniach → Uruchamianie jest przycisk Napraw.

**Silnik CPU** — profile przypisane do ścieżki działają z grami uruchomionymi
jako administrator / z anty-cheatem, CPU Sets poszerzają starą twardą affinity,
rozjazdy są ponownie stosowane co 10 s, ręczne Zastosuj nie jest cofane przez
watcher, poprawiono też kilka błędów edycji profili.

**Tłumaczenie na polski** — presety rdzeni, statusy operacji, dziennik
aktywności, status watchera i przyciski okna działają w wybranym języku. Plik
logu zostaje po angielsku ze stałymi kodami statusów do diagnostyki.

## Aktualizacja

Instalacja `ProcessCoreOptimizer_Setup_2.1.0.exe` na wersji 2.0.0 aktualizuje
ją w miejscu (ta sama tożsamość instalatora i folder). Instalator najpierw
zamyka działający PCO. `%APPDATA%\ProcessCoreOptimizer` nie jest ruszany.

Pełna lista: [CHANGELOG.pl.md](../CHANGELOG.pl.md).
