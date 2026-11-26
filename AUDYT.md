# Audyt PowerOw

## Wydanie 1.01 (v1.0.1)

- Publiczne wydanie jest oznaczone jako stabilne. Aktualizator v1.0 odczytuje tag `v1.0.1`, a interfejs używa go w tytule. SHA-256 instalatora pobranego niezależnie z GitHuba i pliku pobranego wewnątrz aktualizatora były zgodne z metadanymi wydania i plikiem `SHA256SUMS.txt`. [Wydanie 1.01](https://github.com/proszczok2002/PowerOw/releases/tag/v1.0.1)
- W Windows 11 z polskim NVDA 2026.3.1 publiczna wersja 1.0 z włączoną ochroną pokazała ofertę w tle. Użyto Alt+Tab, aby przejść do pytania, i wybrano domyślne **Nie**. Następnie z ustawień wybrano **Sprawdź aktualizacje teraz** i potwierdzono pobieranie. NVDA odczytał postęp pobierania, informację o zweryfikowaniu pliku oraz postęp instalacji. Monit UAC potwierdzono klawiaturą.
- Instalacja zakończyła się przy włączonym Defenderze, a program sam otworzył okno **PowerOw v1.0.1**. Plik aplikacji odpowiadał kompilacji wydania. Zachowały się zadanie z najwyższymi uprawnieniami, stan ochrony i ustawienia użytkownika, w tym interwał 30 minut.
- Zasobnik przekazuje fokus do menu po otwarciu. NVDA odczytał **Menu PowerOw w zasobniku** i **Otwórz PowerOw** przed użyciem strzałki. Wybranie **Ustawienia…** przeniosło fokus na język interfejsu. Wybranie **Sprawdź aktualizacje** odczytało komunikat o aktualnej wersji. Oba polecenia zadziałały przy schowanym głównym oknie.
- Po aktualizacji wyłączono i ponownie włączono autostart i ochronę w ustawieniach. Zweryfikowano rzeczywistą zmianę stanu zadania Harmonogramu i zapisu ochrony, a następnie powrót do pierwotnej konfiguracji.
- Aplikacja przeszła 3510 kontroli, w tym sprawdzenie 197 kluczy w ośmiu językach, dostępnych nazw elementów w 24 zestawieniach języka i motywu oraz walidacji odpowiedzi aktualizatora. Instalator zbudowano z ośmioma językami. Każda wersja językowa README i opisu wydania zawiera nowe polecenia zasobnika.

## Zakres źródeł Windows

Dokumentacja Autorun Organizer opisuje włączanie, wyłączanie, usuwanie i pilnowanie wyłączonych wpisów. Dokumentacja Sysinternals pokazuje także źródła diagnostyczne Windows i sposób filtrowania ich według producenta. Domyślny widok PowerOw obejmuje aplikacje rejestrowane do logowania lub startu komputera. Osobne filtry obejmują zadania i usługi programów. [Autorun Organizer](https://www.chemtable.com/autorun-organizer.htm) [Sysinternals Autoruns](https://learn.microsoft.com/sysinternals/downloads/autoruns)

| Obszar | Ustalenie i implementacja |
| --- | --- |
| Logowanie i widoki rejestru | `Run` jest wykonywany po logowaniu, a `RunOnce` działa jednorazowo. PowerOw czyta także maszynowy widok 32-bitowy. Kopia zachowuje typ i tekst wartości wraz z dosłownymi zmiennymi środowiskowymi. [Run i RunOnce](https://learn.microsoft.com/windows/win32/setupapi/run-and-runonce-registry-keys) [Widoki WOW64](https://learn.microsoft.com/windows/win32/winprog64/registry-redirector) |
| Zasady systemowe i katalogi | Widoczne są pozycje z `Policies\Explorer\Run`. Foldery startowe są odczytywane dla bieżącego konta i wszystkich użytkowników. Skróty oglądane są bez wykonywania. Jeżeli odtworzenie koliduje z aktualnym wpisem, PowerOw zachowuje kopię i zgłasza konflikt. [Konfiguracja autostartu w Windows](https://support.microsoft.com/windows/configure-startup-applications-in-windows-115a420a-0bff-4a6f-90e0-1934c844e473) |
| Harmonogram | Odczyt z COM obejmuje zadania ukryte dostępne dla konta administratora. Zadania mają stan, wyzwalacze i akcje. Dla typów możliwych do odtworzenia bez hasła w kopii jest XML i deskryptor uprawnień. Przy ponownej rejestracji program wyłącza wyzwalacz rejestracji zadania. [Interfejs Harmonogramu](https://learn.microsoft.com/windows/win32/taskschd/task-scheduler-interfaces) [Rejestracja i typy logowania](https://learn.microsoft.com/windows/win32/taskschd/taskfolder-registertaskdefinition) |
| Usługi programów | Osobny filtr pokazuje obsługiwane usługi programów. Wyłączanie przez SCM i przywracanie odtwarzają tryb uruchamiania, w tym flagę opóźnienia. [ChangeServiceConfig](https://learn.microsoft.com/windows/win32/api/winsvc/nf-winsvc-changeserviceconfigw) [Tryb opóźniony](https://learn.microsoft.com/windows/win32/api/winsvc/nf-winsvc-changeserviceconfig2w) |
| MSIX/UWP | Lista pakietów pochodzi z PackageManager. Z manifestów odczytywane są deklaracje `windows.startupTask`. Do przełączania tych wpisów jest dostęp z PowerOw do ustawień Windows. Publiczne API StartupTask zwraca stan z kontekstu pakietu aplikacji. [PackageManager](https://learn.microsoft.com/uwp/api/windows.management.deployment.packagemanager) [StartupTask](https://learn.microsoft.com/uwp/api/windows.applicationmodel.startuptask) |
| Weryfikacja pliku | Odczyt właściwości, skrót SHA-256 i weryfikacja osadzonego podpisu Authenticode z lokalnych danych zaufania. Sprawdzenie dotyczy wskazanego lokalnego pliku. [WinVerifyTrust](https://learn.microsoft.com/windows/win32/api/wintrust/nf-wintrust-winverifytrust) |
| Ochrona | Działania **Wyłącz i pilnuj** oraz **Usuń i pilnuj** zapisują regułę dla konkretnej nazwy i lokalizacji. Kontrola cykliczna usuwa lub wyłącza ponowną rejestrację. Osobno można wstrzymać ochronę albo zdjąć regułę z pojedynczego wpisu. [Tryby ochrony w Autorun Organizer](https://www.chemtable.com/version-history.htm?prod_id=AutorunOrganizer) |

## Decyzje dotyczące uprawnień

Manifest aplikacji wymaga administratora, więc ręczny start jest objęty zwykłym UAC. Przy logowaniu zadanie Harmonogramu uruchamia interfejs z najwyższymi uprawnieniami członka grupy Administratorzy. Operacje dotyczące konta używają tokenu pulpitu. Usługa systemowa czyta reguły systemowe z katalogu zapisywalnego przez administratorów i SYSTEM. [Kontekst bezpieczeństwa Harmonogramu](https://learn.microsoft.com/windows/win32/taskschd/security-contexts-for-running-tasks)

Wybrana lokalizacja instalacji musi umożliwiać ochronę plików uruchamianych z podniesionymi uprawnieniami. Kontrola instalatora sprawdza ścieżkę, ACL, właścicieli katalogów i znacznik istniejącej instalacji. Dezinstalator usuwa skróty, zadanie, usługę i własny folder. Połączenia katalogów napotkane wewnątrz tego folderu odłącza bez usuwania ich celów. Pozostałości zablokowane przez Windows może skierować do usunięcia przy restarcie. [Punkty ponownej analizy w Windows](https://learn.microsoft.com/windows/win32/fileio/reparse-point-operations) [Usuwanie przy restarcie](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-movefileexw)

## Dostępność

W wierszu standardowej listy znajdują się nazwa, stan, typ i zakres. Szczegóły są w polu tylko do odczytu. Akcje są dostępne z przycisków, standardowego menu i skrótów. Menu wpisu i zasobnika przygotowuje dostawców dostępności przed otwarciem i wybiera pierwszą dostępną pozycję po otwarciu. Taka ścieżka odpowiada zgłoszeniu ciszy po otwarciu menu kontekstowego w WinForms. [WinForms #15178](https://github.com/dotnet/winforms/issues/15178) [WinForms #15176](https://github.com/dotnet/winforms/issues/15176) [Powiadomienia Shell](https://learn.microsoft.com/windows/win32/shell/notification-area)

Główna lista grupuje aplikacje, ich zadania logowania i startu oraz deklaracje pakietowe. Podczas wyszukiwania komunikat o stanie ochrony nie przejmuje odczytu listy. Systemowe usługi, sterowniki i źródła przeznaczone do diagnostyki Windows są poza widokiem pracy.

## Wyniki prób autostartu i dezinstalacji

Próby interakcji wykonano w VirtualBox z Windows 11 x64 i polskim NVDA 2026.3.1. Testy autostartu i dezinstalacji powtórzono w różnych konfiguracjach tego środowiska.

- Klawiaturą obsłużono wybór języka, powitanie, wybór folderu, trzy pola dodatkowych zadań i zakończenie. Zadziałały naprawa wcześniejszej instalacji w `C:\Program Files\PowerOw` i świeża instalacja w wybranym `C:\Program Files\PowerOw QA`. Sprawdzono rzeczywisty stan skrótu, zadania Harmonogramu i ochrony po obu ustawieniach.
- Otwarcie skrótu z pulpitu i z menu Start wyświetliło zwykły monit UAC. Po ponownym uruchomieniu komputera włączony autostart uruchomił proces w sesji użytkownika z podniesionym tokenem i bez monitu. Po ponownym uruchomieniu z wyłączonym autostartem proces w sesji użytkownika nie pojawił się.
- Wchodząc do ustawień klawiaturą, włączono samą ochronę przy wyłączonym zadaniu. Wpis przywrócony przez próbę został usunięty. Po wyłączeniu ochrony wpis pozostał w rejestrze. Włączenie samego autostartu zmieniło stan zadania i utrzymało wstrzymaną ochronę.
- NVDA odczytał **Wyłącz** bezpośrednio po Shift+F10 na wierszu i **Otwórz PowerOw** bezpośrednio po klawiszu menu na ikonie w zasobniku. Sprawdzono też wejście do szczegółów, wyłączenie z ochroną, wykrycie powrotu wpisu w dzienniku i przywrócenie. Escape opuścił menu zasobnika, a Enter ponownie otworzył aplikację.
- Dezinstalacja z ustawień Windows zatrzymała PowerOw, usunęła katalog docelowy wraz z dodanymi plikami w podkatalogach, zadanie, usługę, rejestrację i skróty. Plik poza katalogiem wskazany przez dowiązanie katalogowe pozostał dostępny z pierwotną zawartością. Próba powiodła się również w wybranym folderze po wcześniejszym skasowaniu zadania logowania. NVDA odczytał ostrzeżenie o usunięciu całego folderu i komunikat o powodzeniu.
- Przeszły rzeczywiste operacje na wpisach próbnych konta, rejestrze 32-bitowym komputera, folderach autostartu, zadaniach i usłudze z opóźnionym startem.

W próbach z uruchomieniem instalatora lub dezinstalatora przez skrypt testowy Microsoft Defender przerwał działanie procesu tymczasowego Inno Setup i zgłosił `Behavior:Win32/SuspClickFix.G2`. Standardowe uruchomienie instalatora z Eksploratora i dezinstalacja z ustawień Windows zakończyły się pomyślnie przy włączonej ochronie w czasie rzeczywistym i sygnaturach 1.463.478.0. Weryfikacja na żywo dotyczy tej konfiguracji Windows 11 i polskiej mowy NVDA. [Opis zgłaszania błędnych detekcji do Microsoftu](https://learn.microsoft.com/defender-endpoint/defender-endpoint-false-positives-negatives)

## Aktualizacje, kontakt i języki

- Sprawdzono 196 kluczy i zmienne formatu w ośmiu językach. Aplikacja przeszła 3 493 kontrole, w tym 24 kombinacje języka i motywu oraz testy aktualizatora. Kompilator instalatora przetworzył osiem katalogów, osiem wstępów do warunków Windows SDK i lokalizowane komunikaty postępu. README i opis wydania mają polski na początku, a dalej siedem innych wersji językowych.
- W Windows 11 z NVDA 2026.3.1 sprawdzono wybór języka, odczyt strony warunków SDK, jej akceptację i wybory dotyczące skrótu, logowania i ochrony. W aplikacji NVDA odczytał wszystkie przyciski dialogu **Wsparcie i kontakt**. Escape przywrócił fokus na przycisku otwierającym.
- Gdy ochrona była wstrzymana, PowerOw w zasobniku nie pobrał informacji o wydaniu. Powiadomienie pojawiło się po otwarciu okna. Domyślny przycisk **Nie** zakończył pytanie bez przygotowania instalatora. Przy włączonej ochronie pytanie pojawiło się w tle. Gdy Windows zachował fokus w Eksploratorze, Alt+Tab przeniósł do pytania. W ustawieniach NVDA odczytał 15 minut i wybór 30 minut.
- Użyto lokalnej odpowiedzi zastępczej GitHuba oraz rzeczywistego instalatora testowego v1.0.1. NVDA odczytał postęp pobierania i instalacji. Odmowa UAC pozostawiła dotychczasową wersję. Akceptacja UAC pozwoliła zainstalować v1.0.1 i ponownie otworzyć PowerOw z zachowanym autostartem, ochroną i interwałem 30 minut. Monit UAC obsłużono klawiaturą.
- W testach sprawdzono również odrzucenie złej sumy SHA-256 i przekierowania poza dopuszczone adresy. Aktualizator produkcyjny korzysta z najnowszego stabilnego wydania własnego repozytorium. [API GitHub Releases](https://docs.github.com/en/rest/releases/releases#get-the-latest-release)
- Po usunięciu wersji testowej zainstalowano finalny pakiet v1.0 przy aktywnym Defenderze. Sprawdzono tytuł okna i zgodność zbudowanego pliku aplikacji. SHA-256 finalnego instalatora to `3E32F89F5A637AF253C027D2DC8BE9686AEABFBB61E5E952206C7347B3CF7464`.

## Uruchamianie prób

Test `tests/PowerOw.Checks.csproj` sprawdza klucze i zmienne tłumaczeń, 24 połączenia języka i motywu i rzeczywisty odczyt źródeł. Opcja `--guest` wykonuje również rzeczywiste operacje na wpisach próbnych w odizolowanym Windows. Zwykły token wybiera próby konta, a podniesiony token wybiera próby komputera.

Skrypt [GuestUiAudit.ps1](tests/GuestUiAudit.ps1) obsługuje kroki klawiaturą, sprawdza fokus przez UI Automation i zapisuje mowę NVDA. Do próby dezinstalacji służą etapy `PrepareUninstall` i `VerifyUninstall`. Między nimi uruchamia się dezinstalator z listy **Zainstalowane aplikacje**. Etap `ObserveUninstall` może równolegle zapisać odczyt ostrzeżenia i komunikatu końcowego przez NVDA.

Próbę przejścia z publicznej wersji 1.0 do 1.01 odtwarza etap `UpdateRetry`. Użyj `-ExistingUpdateOffer`, gdy oferta po sprawdzeniu w tle jest już widoczna. Monit UAC potwierdź na pulpicie maszyny testowej. Potem `VerifyUpdate` sprawdza ponowne otwarcie okna i zachowanie konfiguracji. Etap `Tray` oczekuje zainstalowanego najnowszego stabilnego wydania i sprawdza oba nowe polecenia zasobnika.

Po ponownym uruchomieniu maszyny skrypt [GuestBoot.ps1](tests/GuestBoot.ps1) sprawdza, czy proces PowerOw pojawił się zgodnie z ustawieniem autostartu, czy jego token ma podniesione uprawnienia oraz czy jest widoczny monit UAC.
