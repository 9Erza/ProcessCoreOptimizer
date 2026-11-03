# Lista zmian

## Process Core Optimizer 2.0.0

**Data wydania: 2026-11-03** · Windows 10/11 x64 · Instalator: `ProcessCoreOptimizer_Setup_2.0.0.exe`

Największa aktualizacja PCO od wersji 1.3.0. Optymalizacja procesów jest teraz
bezpieczniejsza (zweryfikowana tożsamość przed każdą zmianą), łatwiejsza w
automatyzacji (Smart Rules, Profile Studio, EcoQoS), lżejsza w obserwacji
(Live Insights, mini konsola aktywności), a całe środowisko — nawigacja,
Developer Hub, lokalizacja polska/angielska, aktualizacje — dopracowano do
codziennego użytku.

### Najważniejsze zmiany

- **Process Explorer 2.0** — zgrupowane procesy i pojedyncze instancje z podglądem.
- **Profile Studio** — wygodniejsze tworzenie profili i zarządzanie nimi.
- **Smart Rules** — automatyzacja przy starcie procesu i aktywnym oknie.
- **EcoQoS** — opcjonalne mechanizmy wydajności Windows, nigdy automatyczne.
- **Live Insights** — lekkie wykresy historii CPU i pamięci.
- **Niezawodność** — weryfikowana tożsamość przy każdej zmianie, przywracanie sesji, reset fabryczny.
- **Interfejs** — kompaktowy sidebar, Developer Hub, pełne PL/EN.

### Dodano

- Przełącznik widoku Grupy / Instancje z podglądem instancji (PID, czas startu,
  ścieżka EXE, CPU, RAM, priorytet, przypisany profil) z jednego skanowania.
- Edytor profili (Profile Studio): nazwa, ścieżka EXE z przeglądaniem,
  walidowana maska affinity (hex), priorytet, Affinity/CPU Sets/EcoQoS, flagi
  stosowania, podpowiedzi path-bound, bezpieczne szkice; duplikowanie, import
  i eksport (walidowany JSON).
- Silnik Smart Rules: wyzwalacze przy starcie (opcjonalne opóźnienie) i na
  aktywnym oknie (debounce dla Alt+Tab), cooldowny, deterministyczna kolejność,
  pauza, feed ostatniej aktywności ("dlaczego reguła zadziałała"), czyszczenie
  obejść dla reguł.
- Tryb EcoQoS przez oficjalne API Windows (Windows 10 1809+): jawne wł./wył./
  bez zmian na profil, zweryfikowany cykl włącz-wyłącz-przywróć.
- Live Insights: wykresy historii CPU, working set i pamięci prywatnej w panelu
  szczegółów; pierwsza próbka CPU i brak danych raportowane uczciwie jako
  Unknown, nigdy jako zero.
- Zakładka Diagnostyka: wyjaśnienie "Dlaczego ten profil?", aktywność reguł,
  anonimizowany eksport ZIP na żądanie (ścieżki użytkownika zamaskowane, bez
  notatek).
- Developer Hub w sidebarze: zwijana karta `PCO <wersja>` z oficjalnymi linkami
  (repozytorium, 9Erza, DobryPC.pl, Buy Me a Coffee) i nawigacją do About
  w aplikacji.
- Mini konsola aktywności w sidebarze (kropka statusu + ostatni komunikat,
  kliknięcie otwiera Diagnostykę) zamiast wielkiej listy logów.
- Okno Dostępna aktualizacja (ciemny motyw PCO, ikona PCO, PL/EN) oraz ręczne
  "Sprawdź aktualizacje" z uczciwymi statusami w Ustawieniach.
- Kreator pierwszego uruchomienia (powitanie, Affinity vs CPU Sets, start)
  i zreorganizowane Ustawienia (Ogólne / Uruchamianie i uprawnienia /
  Monitorowanie / Monitor sprzętu / Diagnostyka / Aktualizacje / Dane aplikacji
  i reset / O programie).

### Zmieniono

- Nowy ciemny design system (tokeny `Themes/`) we wszystkich siedmiu widokach.
- Poprawne sortowanie numeryczne, rozszerzone filtry (nazwa + ścieżka, tylko
  zoptymalizowane, z profilem) i stabilne odświeżanie bez resetu scrolla i selekcji.
- Przywracanie ustawień sprzed optymalizacji, weryfikowane na instancję
  (przetworzone PID-y nigdy nie dostają cudzych ustawień).
- Rejestracja autostartu stosowana tylko przy zmianie ustawień startu; realny
  stan autostartu (Wyłączony / Rejestr / Harmonogram / Konflikt) widoczny
  w Ustawieniach.
- Aktualizacje powiadamiają tylko o prawdziwym opublikowanym wydaniu z dokładnym
  instalatorem — sam podbity `version.txt` nigdy nie powiadamia; automatyczne
  wyniki milczą, gdy nie ma aktualizacji; brak modali nad grami i zminimalizowanymi oknami.
- Monitor sprzętu pozostaje wyłączony do jawnego włączenia; stan OFF pokazuje
  zwartą kartę pustego stanu zamiast pustego widoku.
- Sidebar: smuklejszy scrollbar nawigacji, kompaktowy branding, mini konsola;
  pełna parzystość PL/EN egzekwowana testami.

### Naprawiono

- Nieaktualne snapshoty nie autoryzują już zmian: PID + czas startu + nazwa
  (+ ścieżka EXE dla profili path-bound) są weryfikowane na świeżym uchwycie OS
  przed każdym zapisem (`SKIPPED_IDENTITY_MISMATCH`, także przy recyklingu PID).
- Zmiany priorytetu nie odpalają już z samego przeglądania listy; Apply, Save
  i Update pozostały jawnymi punktami mutacji.
- Profile tylko-priorytetowe nie są już odrzucane przez sanityzację.
- Mapowanie checkboxów rdzeni używa indeksu procesora logicznego, nie pozycji na liście.
- Update/Delete profili rozstrzygane po stabilnym `Profile.Id`; zapis samej nazwy
  zachowuje rodzeństwo path-bound.
- Błędy affinity pokazują jawnie nieudane czyszczenie CPU Sets zamiast chować je
  za statusem sukcesu.
- Start single-instance przetrwa crash zamiast padać na porzuconym mutexie.
- Wywołania harmonogramu mają timeout zamiast wieszać UI.
- Zamknięto wyciek uchwytu procesu w detekcji sprzętu.

### Bezpieczeństwo i niezawodność

- Każdy zapis do OS wymaga zweryfikowanej tożsamości procesu; niekontrolowane
  ścieżki PID-only usunięto; procesy chronione są odmawiane z podaniem przyczyny.
- Częściowy sukces jest raportowany uczciwie (`FullyApplied` / `PartiallyApplied` /
  `Failed` / pominięcia) i ponawiany — nigdy cachowany jako wykonany.
- Priorytet RealTime pozostaje za jawną zgodą i dialogiem potwierdzenia.
- Ustawienia, profile i reguły zapisywane atomowo z kopią `.bak`; odczyt cofa się
  do kopii przy uciętym JSON.
- Start działa plan → weryfikacja z kolejnością create-before-delete; anulowanie
  UAC i nieudane utworzenia pokryte testami.
- Reset fabryczny (wpisane `RESET`) czyści ustawienia, profile, kopie, logi i cache,
  czyści pamięć reguł i nie odpala reguł w trakcie.
- Watcher w tle: ochrona przed nakładaniem się, anulowanie i strażniki zamykania,
  ograniczone cache, throttled logowanie.
- Linki zewnętrzne (repozytorium, autor, strona, wsparcie) na allowliście, otwierane
  tylko po kliknięciu, bez trackingu i bez żądań sieciowych samej aplikacji.
- 240 testów automatycznych (237 zielonych + 3 warunkowe), suita GUI 21/21,
  walidacja instalatora 8/8.

### Wydajność

- Brak nowych timerów dla UI, updatera i konsoli: mini konsola podąża za istniejącymi
  zdarzeniami, checki maksymalnie raz na uruchomienie.
- Pełne skany tylko przy widocznym zakładce Procesy; watcher skanuje tylko procesy
  z włączonymi profilami; czujniki sprzętu OFF do włączenia.
- Typowy idle (zminimalizowany): ~0,16% CPU, ~144 MB working set na maszynie
  referencyjnej. Bez obietnic FPS: PCO przypina pracę do rdzeni, nie podkręca sprzętu.

### Instalacja

- Tylko Windows 10/11 x64. Jeden oficjalny asset: `ProcessCoreOptimizer_Setup_2.0.0.exe`
  (Inno Setup, skrót Start Menu, opcjonalna ikona pulpitu, kreator EN+PL, czysty wpis
  w Programach; dane użytkownika nietknięte przy deinstalacji).
- Przed instalacją zweryfikuj SHA-256 z opisu GitHub Release.
- Instalator nie jest jeszcze podpisany kodem EV: SmartScreen może poprosić o
  potwierdzenie przy pierwszym uruchomieniu — to oczekiwane i udokumentowane.

### Migracja z 1.3.0

- **1.3.0 nie aktualizuje się w miejscu.** Tożsamość instalatora nie jest odtwarzalna
  z tego repozytorium (zbudowany zewnętrznie), więc 2.0.0 używa nowej tożsamości
  i widnieje jako osobny wpis — to zamierzone, nie błąd.
- Zalecana ścieżka: najpierw odinstaluj 1.3.0, potem zainstaluj 2.0.0.
- Dane zostają: `%APPDATA%\ProcessCoreOptimizer` (profile, ustawienia, reguły) nie
  jest ruszane przez deinstalację ani instalację, a profile/ustawienia 1.3.x wczytują
  się bez zmian w 2.0.0 (legacy `Exclusive` migruje do `Affinity`).
- Uwaga: zainstalowane binaria 1.3.0 sprawdzają tylko `version.txt`, więc mogą
  zgłosić 2.0.0 i wskazać stronę wydań. Instaluj 2.0.0 bezpośrednio z assetu wydania;
  automatyczna migracja 1.3 → 2.0 nie istnieje.

### Znane ograniczenia

- UI obejmuje grupę procesorów 0 (do 64 procesorów logicznych) projektowo; większe
  maszyny są raportowane w aplikacji, nie obsługiwane po cichu.
- Bez sterownika kernel, bez wstrzykiwania kodu, bez omijania anti-cheat; procesy
  chronione są odmawiane z przyczyną. Zgodność z grami online niegwarantowana.
- Instalacja/deinstalacja na zapasowej maszynie jest udokumentowana, lecz nie była
  wykonana przez automatyzację; obowiązuje ręczna procedura odbioru z `docs/INSTALLER.md`.

## Poprzednie wydania

### v1.3.0 - Refaktoryzacja jądra i fundamenty wydajności

### Architektura
- Dodano `ProcessScannerService` do oddzielenia pełnych skanów UI od lekkiego watchera profili.
- Dodano `OptimizationService` ze strukturalnymi wynikami i cache na instancję procesu.
- Dodano stabilny `ProcessInstanceKey` (PID + czas startu) przeciw recyklingowi PID w Windows.
- Dodano `UpdateService`, wyprowadzając sprawdzanie aktualizacji z `MainViewModel`.
- Dodano `LocalizationService` jako krok ku pełnej lokalizacji z zasobów.
- Dodano `AtomicFileService` dla bezpieczniejszego zapisu JSON.

### Wydajność
- Pełne skanowanie listy procesów tylko przy widocznej zakładce Procesy.
- Watcher w tle skanuje tylko procesy z włączonymi profilami.
- Liczniki wydajności per core inicjalizowane leniwie.
- Monitor sprzętu inicjalizowany leniwie, dopiero po otwarciu zakładki.
- Czujniki dysków domyślnie wyłączone (brak zbędnego odpytywania).
- Włączona wirtualizacja wierszy i kolumn DataGrid.

### Stabilność
- Dodano strażnika single-instance (nazwany mutex).
- Bezpieczniejsze parsowanie argumentów startu: `--minimized`, `--tray`, `--no-update-check`, `--safe-mode`.
- Profile i ustawienia JSON zapisywane atomowo z fallbackiem `.tmp` i `.bak`.
- Logger rotuje duże pliki logów.
- Wersja czytana z metadanych assembly zamiast hardcode w ViewModelu.

### Profile
- Schemat profili v2 z polami na przyszłość: ID, nazwa, opcjonalna ścieżka EXE, stemple czasu, notatki i przełączniki per akcja.
- Starsze profile pozostają kompatybilne.
- Legacy `Exclusive` nadal normalizowane do `Affinity`.

### Ustawienia
- Dodano przełącznik sprawdzania aktualizacji.
- Dodano zaawansowany przełącznik czujników dysków.
