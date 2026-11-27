# PowerOw

## Polski

PowerOw to menedżer autostartu Windows z obsługą klawiatury i czytników ekranu. Instalator i aplikacja mają polski, angielski, niemiecki, rosyjski, chorwacki, francuski, hiszpański i chiński uproszczony. W ustawieniach aplikacji są jasny, granatowy i systemowy motyw. Przy włączonym motywie kontrastowym aplikacja korzysta z kolorów Windows.

### Instalacja

1. Pobierz `PowerOw-Setup.exe` dla Windows 10 22H2 lub Windows 11 x64 i porównaj SHA-256 z `SHA256SUMS.txt` z tego samego wydania. Uruchom instalator i potwierdź monit administratora. Dla niepodpisanej kompilacji Windows wyświetla nazwę **Nieznany wydawca**. [Pobierz najnowsze wydanie](https://github.com/proszczok2002/PowerOw/releases/latest)
2. Wybierz język, przejdź z powitania dalej i zapoznaj się z warunkami dołączonych składników Windows SDK. Po ich zaakceptowaniu wybierz folder instalacji. Domyślnie jest to `%ProgramFiles%\PowerOw`. Inny folder na lokalnym dysku musi być pusty, nowy albo zawierać istniejącą instalację PowerOw. Jego ścieżka musi być chroniona przed zmianą przez zwykłe konta.
3. Na ekranie dodatkowych zadań Spacją przełącz skrót na pulpicie, start PowerOw po zalogowaniu i ochronę wpisów. To trzy niezależne wybory. Użyj **Dalej**, a następnie **Instaluj**. Na końcu możesz od razu uruchomić PowerOw lub otworzyć go później z menu Start.

Uruchomienie ze skrótu Start lub pulpitu wywołuje systemowy monit UAC. PowerOw pokazuje wpisy konta i komputera w tym samym oknie administratora. Zadanie systemowe `\PowerOw` uruchamia go przy logowaniu członka grupy Administratorzy z najwyższymi uprawnieniami, bez kolejnego monitu. Oddzielna usługa pilnuje wpisów systemowych również przy zamkniętym oknie. [Kontekst zadań Harmonogramu](https://learn.microsoft.com/windows/win32/taskschd/security-contexts-for-running-tasks)

W **Ustawieniach PowerOw** wybierz **Uruchamiaj PowerOw przy logowaniu do Windows** i **Pilnuj wpisów autostartu** niezależnie od siebie. Pierwsze pole zmienia stan zadania Harmonogramu. Drugie wstrzymuje lub wznawia reguły bieżącego konta i komputera. Po ponownym włączeniu ochrony reguły zapisane wcześniej znów działają. Skrót do ustawień to **Ctrl+,**.

### Aktualizacje

Wydanie **1.02** rozszerza powiadomienia o nowych wpisach autostartu. Aby przejść z 1.01, wybierz **Sprawdź aktualizacje** w zasobniku albo poczekaj na sprawdzenie w tle przy włączonej ochronie. W pytaniu i po instalacji zobaczysz numer **v1.0.2**. [Wydanie 1.02](https://github.com/proszczok2002/PowerOw/releases/tag/v1.0.2)

Przy włączonej ochronie PowerOw sprawdza najnowsze stabilne wydanie z GitHuba także w zasobniku i pokazuje okno **Tak / Nie**. Jeśli Windows pozostawi fokus w bieżącej aplikacji, do pytania o aktualizację przejdziesz przez **Alt+Tab**. Przy wstrzymanej ochronie sprawdzenie następuje po otwarciu głównego okna. W ustawieniach możesz wyłączyć automatyczne sprawdzanie, zmienić częstotliwość sprawdzania w tle (domyślnie 15 minut) lub wybrać **Sprawdź aktualizacje teraz**.

Po wybraniu **Tak** otworzy się okno postępu. Tab prowadzi przez stan pobierania, pasek postępu i przycisk **Anuluj**. PowerOw weryfikuje rozmiar i SHA-256 instalatora przed monitem UAC. Po zaakceptowaniu UAC instalator pokazuje postęp i wznawia PowerOw z zachowanymi ustawieniami. Przyciski i okna aktualizacji obsługują klawiaturę. Jeśli potrzebna jest akceptacja nowych warunków składników SDK, uruchom pełny kreator z najnowszego wydania.

### Zarządzanie autostartem

Domyślny widok **Programy startujące z Windows** obejmuje wpisy logowania, aplikacje pakietowe i zadania programów z wyzwalaczem startu lub logowania. Filtr **Źródło autostartu** daje też dostęp do pozostałych zadań i usług programów.

- Strzałkami wybierz wpis na liście. W wierszu czytnik otrzymuje nazwę, stan, typ i zakres wpisu. Naciśnij **Enter** lub **F6**, aby przejść do pola ze szczegółami. **Ctrl+F** przenosi do wyszukiwania, a **F5** odświeża listę.
- **Ctrl+D** wyłącza wpis, a **Delete** usuwa go z autostartu. Przy obu działaniach PowerOw zachowuje kopię do przywrócenia. **Ctrl+E** przywraca kopię lub ponownie włącza obsługiwane zadanie. Zaznaczaj wiele pozycji klawiszem Shift ze strzałkami. **Ctrl+A** na liście zaznacza wyniki bieżącego filtra.
- **Ctrl+G** wyłącza i pilnuje wpisu. **Ctrl+Shift+G** usuwa go i pilnuje. Reguła sprawdza nazwę w tej samej lokalizacji mniej więcej co pięć sekund. Obejmuje też powrót wpisu ze zmienionym poleceniem. W menu kontekstowym wybierz **Przestań pilnować wpisu**, aby zdjąć regułę. Przywrócenie kopii również wyłącza regułę.
- Otwórz menu wpisu klawiszem menu kontekstowego lub **Shift+F10**. Znajdziesz tam opóźnienie startu, szczegóły pliku i podpisu, kopiowanie szczegółów i przejście do lokalizacji. **Ctrl+H** otwiera dziennik, w którym są m.in. działania ochrony i błędy operacji.
- W kolumnie tekstowej wiersza **To konto** oznacza wpis bieżącego pulpitu, a **Ten komputer** wpis systemowy. Dziennik łączy działania bieżącego konta i komputera. Stan usługi jest widoczny pod listą.

### Źródła

| Źródło | Obsługa w PowerOw |
| --- | --- |
| Rejestr `Run`, `RunOnce` i `Policies\Explorer\Run` bieżącego konta oraz komputera | Wyłączanie, usuwanie, przywracanie i ochrona edytowalnych wpisów. `Run` i `RunOnce` komputera są odczytywane w obu widokach rejestru. Wspólny klucz zasad jest widoczny do odczytu. |
| Startup konta i wspólny folder Startup | Te same operacje dla zwykłych plików i skrótów. Kopia zachowuje zawartość pliku. |
| Harmonogram zadań | Przegląd wyzwalaczy i akcji, wyłączanie i ponowne włączanie zadań spoza chronionych lokalizacji. Usuwanie z kopią XML i uprawnień dla obsługiwanych typów logowania. |
| Usługi | Wyłączanie i przywracanie trybu startu usług spoza chronionych składników, również z opóźnionym startem. Zmiana dotyczy konfiguracji następnego uruchomienia usługi. |
| Aplikacje MSIX/UWP z deklaracją `StartupTask` | Wgląd w nazwę zadania, pakiet i lokalizację manifestu. **Otwórz lokalizację** otwiera systemową listę i przełączniki dla tych aplikacji. |

**Opóźnij uruchomienie** używa Harmonogramu zadań dla dostępnych lokalnych programów EXE z klucza `Run` bieżącego konta. Wybierz od 5 do 600 sekund. **Przywróć lub włącz** usuwa opóźnienie i odtwarza pierwotną wartość. [Zadania logowania Microsoftu](https://learn.microsoft.com/windows/win32/taskschd/logontrigger)

Ochrona dotyczy zapisanej rejestracji autostartu. Może upłynąć kilka sekund między dodaniem przez inny program a jej usunięciem. Nowa nazwa lub lokalizacja to osobny wpis. Dla konfiguracji oznaczonych jako **Wyłączony w Windows** ustawienia systemu dają dostęp do przełącznika Windows. [Autostart w Windows](https://support.microsoft.com/windows/configure-startup-applications-in-windows-115a420a-0bff-4a6f-90e0-1934c844e473) [Run i RunOnce](https://learn.microsoft.com/windows/win32/setupapi/run-and-runonce-registry-keys)

### Powiadomienia o nowych wpisach

Zaznacz **Pilnuj wpisów autostartu** i zostaw PowerOw uruchomiony. Gdy pojawi się nowy wpis, program wysyła powiadomienie Windows także przy zamkniętym głównym oknie. Sprawdza rejestr, oba foldery Startup, zadania z wyzwalaczem startu lub logowania, automatycznie uruchamiane usługi aplikacji oraz nowe deklaracje `StartupTask` pakietów MSIX/UWP. Sprawdzenie odbywa się mniej więcej co 20 sekund. Przy uruchomieniu PowerOw i wznowieniu ochrony bieżące wpisy są stanem początkowym.

NVDA może odczytać powiadomienie bez przenoszenia fokusu z bieżącej aplikacji. Gdy Windows wyciszy lub ukryje baner, w głównym oknie naciśnij **Ctrl+H**, aby odczytać nazwę i lokalizację wykrytego wpisu. [Powiadomienia Windows](https://learn.microsoft.com/windows/win32/shell/notification-area)

### Zasobnik

Naciśnij **Windows+B**. Strzałkami znajdź **PowerOw — Ochrona autostartu** lub otwórz **Pokaż ukryte ikony**. **Enter** otwiera okno. Klawisz menu kontekstowego lub **Shift+F10** otwiera polecenia **Otwórz PowerOw**, **Odśwież**, **Ustawienia…**, **Sprawdź aktualizacje**, **Wstrzymaj ochronę** lub **Wznów ochronę** oraz **Zakończ PowerOw**. Ustawienia i wynik sprawdzania aktualizacji możesz otworzyć bezpośrednio z zasobnika. Zamknięcie okna przez **Alt+F4** chowa je do zasobnika. Wybranie **Zakończ PowerOw** zatrzymuje ochronę konta do kolejnego uruchomienia. Systemowa usługa ma własny stan ochrony. [Powiadomienia i zasobnik Windows](https://learn.microsoft.com/windows/win32/shell/notification-area)

### Dezinstalacja

Wybierz **PowerOw** w **Ustawienia → Aplikacje → Zainstalowane aplikacje** i uruchom dezinstalację. Dezinstalator zatrzymuje proces i usługę, usuwa zadanie uruchamiania PowerOw, skróty oraz cały wybrany folder instalacji wraz z plikami dodanymi do niego. Tam znajdują się również kopie wpisów systemowych. Jeżeli potrzebujesz z nich cofnąć zmianę, użyj **Przywróć lub włącz** przed odinstalowaniem.

Jeśli wcześniejsza kompilacja PowerOw przerywa dezinstalację, instalacja aktualnej kompilacji w tym samym folderze podmienia również dezinstalator.

Kopie wpisów konta i ustawienia są w `%LocalAppData%\PowerOw`, dzięki czemu można użyć ich ponownie po reinstalacji. Utworzone opóźnienia są zadaniami Windows. Polecenie **Przywróć lub włącz** usuwa wybrane opóźnienie.

### Wsparcie i kontakt

Przycisk **Wsparcie i kontakt** w głównym oknie oraz polecenie w menu **Pomoc** prowadzą do strony autora, zgłoszeń, repozytorium i dobrowolnego wsparcia. [Zgłoś problem](https://github.com/proszczok2002/PowerOw/issues) [Strona autora](https://proszczok.pl/) [PayPal](https://paypal.me/proszczok) [Revolut](https://revolut.me/r_rosiejka)

### Budowanie i licencje

Na Windows zainstaluj .NET SDK 10 oraz Inno Setup 6.7.2. W razie innej lokalizacji kompilatora podaj jego ścieżkę parametrem `-Iscc`. [Pobierz .NET SDK](https://dotnet.microsoft.com/download/dotnet/10.0) [Pobierz Inno Setup 6.7.2](https://github.com/jrsoftware/issrc/releases/tag/is-6_7_2)

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\tools\Build.ps1 -Iscc "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
dotnet run --project tests/PowerOw.Checks.csproj -c Release
```

Kompilacja zapisuje instalator i jego SHA-256 w `dist`. Testy struktury interfejsu i aktualizatora działają bez instalowania programu. Kod PowerOw jest dostępny na licencji MIT. [Audyt](AUDYT.md) [Licencja MIT](LICENSE) [Licencje zależności](THIRD-PARTY-NOTICES.md)

## English

Run **PowerOw-Setup.exe** and approve the administrator prompt. Choose a language, installation folder and which options to enable for the desktop shortcut, logon startup and protection. The default folder is `%ProgramFiles%\PowerOw`. Start and desktop shortcuts request elevation. An enabled scheduled task starts the elevated notification area app at an administrator’s sign-in.

Review and accept the Windows SDK component terms on first installation. Compare the downloaded installer’s SHA-256 against the checksum from the same release. [Latest release](https://github.com/proszczok2002/PowerOw/releases/latest)

Use **Ctrl+F** to search, **F5** to refresh, and **F6** to read details. **Ctrl+D** disables, **Delete** removes, **Ctrl+E** restores or enables, **Ctrl+G** disables and protects, and **Ctrl+Shift+G** removes and protects. **Ctrl+H** opens the activity log. **Ctrl+,** opens settings, where you can control logon startup and protection independently.

Open the notification area with **Windows+B** and find PowerOw or the hidden icons. Press **Enter** to open the main window or **Shift+F10** for commands such as **Settings…** and **Check for updates**. Uninstall through Windows Settings. This removes the installation directory, its system backups, the service, the PowerOw logon task and shortcuts. Use Restore before uninstalling if you need the system backups.

With protection enabled, PowerOw checks for stable releases in the background every 15 minutes by default and asks before updating. With protection paused, it checks when you open the main window. Change the interval, disable automatic checks or use **Check for updates now** in Settings. Select **Yes**, follow the accessible download progress and approve Windows UAC. Installation then shows progress and restarts PowerOw with your settings.

While protection is on, PowerOw notifies you of new Registry and Startup folder entries, boot and logon tasks, automatic application services, and newly registered MSIX/UWP startup declarations. It works from the notification area. Use **Ctrl+H** in the main window to read each entry’s name and location.

Use **Support and contact** for the issue tracker, the author’s site and optional donations. Build on Windows using .NET SDK 10 and Inno Setup 6.7.2. [Report a problem](https://github.com/proszczok2002/PowerOw/issues) [Author’s site](https://proszczok.pl/) [MIT license](LICENSE) [Third-party notices](THIRD-PARTY-NOTICES.md)

## Deutsch

PowerOw verwaltet den Windows-Autostart und kann Einträge deaktivieren, entfernen, wiederherstellen und vor einer erneuten Registrierung schützen. Laden Sie `PowerOw-Setup.exe` herunter, vergleichen Sie die SHA-256-Prüfsumme und bestätigen Sie die Administratorabfrage. Wählen Sie Sprache und Zielordner, akzeptieren Sie die Bedingungen für die Windows-SDK-Komponenten und wählen Sie die Optionen für Desktopverknüpfung, Anmeldestart und Schutz. [Neueste Version](https://github.com/proszczok2002/PowerOw/releases/latest)

Mit aktivem Schutz prüft PowerOw standardmäßig alle 15 Minuten im Hintergrund auf Updates. Bei pausiertem Schutz erfolgt die Prüfung beim Öffnen des Hauptfensters. In den Einstellungen können Sie das Intervall ändern, die Automatik abschalten oder **Jetzt nach Updates suchen** wählen. Nach **Ja** zeigt PowerOw den Downloadfortschritt an. Bestätigen Sie die Windows-Abfrage für Administratorrechte, um die Installation zu starten.

Mit **Strg+F** suchen Sie, mit **F6** lesen Sie Details und mit **Windows+B** erreichen Sie den Infobereich. Öffnen Sie am PowerOw-Symbol mit **Umschalt+F10** das Menü mit **Einstellungen…** und **Nach Updates suchen**. Über **Unterstützung und Kontakt** erreichen Sie die Projektseite, Fehlermeldungen und freiwillige Spenden. Deinstallieren Sie PowerOw über **Einstellungen → Apps**. [Problem melden](https://github.com/proszczok2002/PowerOw/issues) [MIT-Lizenz](LICENSE)

Bei aktivem Schutz meldet PowerOw neue Autostarteinträge auch aus dem Infobereich. Mit **Strg+H** öffnen Sie das Aktivitätsprotokoll mit Namen und Speicherort der erkannten Einträge.

## Русский

PowerOw управляет автозагрузкой Windows. Записи можно отключать, удалять, восстанавливать и защищать от повторного добавления. Скачайте `PowerOw-Setup.exe`, сравните контрольную сумму SHA-256 и подтвердите запрос прав администратора. Выберите язык, папку установки и параметры ярлыка, запуска при входе и защиты. Ознакомьтесь с условиями использования компонентов Windows SDK и примите их. [Последний выпуск](https://github.com/proszczok2002/PowerOw/releases/latest)

При включённой защите PowerOw по умолчанию проверяет обновления в фоне каждые 15 минут. При приостановленной защите проверка выполняется при открытии главного окна. В настройках можно изменить интервал, отключить автоматическую проверку или проверить обновления вручную. После ответа **Да** PowerOw показывает ход загрузки. Для начала установки подтвердите запрос Windows на права администратора.

**Ctrl+F** открывает поиск, **F6** переводит к подробностям, **Windows+B** — в область уведомлений. Выберите значок PowerOw и нажмите **Shift+F10**, чтобы открыть меню с пунктами **Настройки…** и **Проверить обновления**. В разделе **Поддержка и контакты** есть ссылки на проект, сообщения о проблемах и добровольную поддержку. Удаление доступно в параметрах Windows в разделе приложений. [Сообщить о проблеме](https://github.com/proszczok2002/PowerOw/issues) [Лицензия MIT](LICENSE)

При включённой защите PowerOw сообщает о новых записях автозагрузки и при работе в области уведомлений. **Ctrl+H** открывает журнал с названиями и расположением обнаруженных записей.

## Hrvatski

PowerOw upravlja automatskim pokretanjem u sustavu Windows. Unose možete onemogućiti, ukloniti, vratiti i zaštititi od ponovnog dodavanja. Preuzmite `PowerOw-Setup.exe`, provjerite SHA-256 i potvrdite administratorski upit. Odaberite jezik i odredišnu mapu, prihvatite uvjete za komponente Windows SDK-a te odaberite prečac, pokretanje pri prijavi i zaštitu. [Najnovije izdanje](https://github.com/proszczok2002/PowerOw/releases/latest)

Kad je zaštita uključena, PowerOw zadano provjerava ažuriranja u pozadini svakih 15 minuta. Kad je zaštita pauzirana, provjerava ih pri otvaranju glavnog prozora. U postavkama možete promijeniti interval, isključiti automatsku provjeru ili odmah provjeriti ažuriranja. Odaberite **Da** za preuzimanje i potvrdite Windowsov upit za početak instalacije.

**Ctrl+F** otvara pretraživanje, **F6** pojedinosti, a **Windows+B** područje obavijesti. Odaberite ikonu PowerOw i pritisnite **Shift+F10** za izbornik s naredbama **Postavke…** i **Provjeri ažuriranja**. **Podrška i kontakt** sadrži poveznice na projekt, prijavu problema i dobrovoljnu podršku. Program možete deinstalirati u postavkama sustava Windows. [Prijavi problem](https://github.com/proszczok2002/PowerOw/issues) [Licenca MIT](LICENSE)

Dok je zaštita uključena, PowerOw prikazuje obavijesti o novim unosima automatskog pokretanja i kada radi u području obavijesti. Pritisnite **Ctrl+H** za nazive i lokacije otkrivenih unosa u dnevniku aktivnosti.

## Français

PowerOw gère les entrées de démarrage de Windows. Vous pouvez les désactiver, les supprimer, les restaurer et empêcher leur réapparition. Téléchargez `PowerOw-Setup.exe`, vérifiez son empreinte SHA-256 et acceptez la demande d’autorisation. Choisissez la langue et le dossier, acceptez les conditions des composants du SDK Windows, puis sélectionnez le raccourci, le lancement à l’ouverture de session et la protection. [Dernière version](https://github.com/proszczok2002/PowerOw/releases/latest)

Lorsque la protection est active, PowerOw recherche les mises à jour en arrière-plan toutes les 15 minutes par défaut. En pause, la recherche a lieu à l’ouverture de la fenêtre principale. Modifiez l’intervalle, désactivez la recherche automatique ou lancez une recherche immédiate dans les paramètres. Choisissez **Oui**, suivez la progression du téléchargement, puis acceptez la demande d’administration de Windows pour lancer l’installation.

**Ctrl+F** lance la recherche, **F6** ouvre les détails et **Windows+B** donne accès à la zone de notification. Sur l’icône de PowerOw, appuyez sur **Maj+F10** pour ouvrir le menu avec **Paramètres…** et **Rechercher les mises à jour**. **Soutien et contact** donne accès au projet, aux signalements et aux dons volontaires. La désinstallation se fait dans les paramètres de Windows. [Signaler un problème](https://github.com/proszczok2002/PowerOw/issues) [Licence MIT](LICENSE)

Lorsque la protection est active, PowerOw signale les nouvelles entrées de démarrage même lorsqu’il reste dans la zone de notification. **Ctrl+H** ouvre le journal d’activité avec le nom et l’emplacement des entrées détectées.

## Español

PowerOw gestiona las entradas de inicio de Windows. Permite desactivarlas, eliminarlas, restaurarlas y protegerlas para que no vuelvan a aparecer. Descarga `PowerOw-Setup.exe`, comprueba el SHA-256 y acepta el aviso de administrador. Elige el idioma y la carpeta, acepta las condiciones de los componentes del SDK de Windows y selecciona el acceso directo, el inicio de sesión y la protección. [Última versión](https://github.com/proszczok2002/PowerOw/releases/latest)

Con la protección activa, PowerOw busca actualizaciones en segundo plano cada 15 minutos de forma predeterminada. Con la protección en pausa, busca al abrir la ventana principal. En la configuración puedes cambiar el intervalo, desactivar la búsqueda automática o buscar ahora. Elige **Sí**, revisa el progreso de descarga y confirma la solicitud de administrador de Windows para instalar.

**Ctrl+F** abre la búsqueda, **F6** los detalles y **Windows+B** el área de notificación. Selecciona el icono de PowerOw y pulsa **Mayús+F10** para abrir el menú con **Configuración…** y **Buscar actualizaciones**. **Apoyo y contacto** enlaza con el proyecto, los informes de problemas y las donaciones voluntarias. Desinstala el programa desde la configuración de Windows. [Informar de un problema](https://github.com/proszczok2002/PowerOw/issues) [Licencia MIT](LICENSE)

Con la protección activa, PowerOw avisa de las nuevas entradas de inicio incluso desde el área de notificación. Pulsa **Ctrl+H** para ver el nombre y la ubicación de cada entrada detectada en el registro de actividad.

## 简体中文

启用保护后，即使 PowerOw 在通知区域后台运行，也会报告新添加的启动项。按 **Ctrl+H** 可在活动日志中查看检测到的启动项名称及其位置。

PowerOw 用于管理 Windows 启动项。您可以禁用、删除和恢复启动项，也可以启用保护以阻止它们重新出现。下载 `PowerOw-Setup.exe` 后，请核对 SHA-256 校验值并确认管理员权限。选择语言和安装目录，接受 Windows SDK 组件条款，然后选择桌面快捷方式、登录时启动和启动项保护。 [最新版本](https://github.com/proszczok2002/PowerOw/releases/latest)

保护已启用时，PowerOw 默认每隔 15 分钟在后台检查更新。暂停保护后，打开主窗口时才会检查。您可以在设置中更改间隔、关闭自动检查或立即手动检查。选择**是**后，您可以查看下载进度。下载验证成功后，请在 Windows 对话框中确认管理员权限以开始安装。

按 **Ctrl+F** 搜索，按 **F6** 查看详细信息，按 **Windows+B** 前往通知区域。选中 PowerOw 图标后按 **Shift+F10**，即可打开包含**设置…**和**检查更新**的菜单。**支持与联系**提供项目、问题反馈及自愿捐助的链接。可从 Windows 设置卸载程序。 [报告问题](https://github.com/proszczok2002/PowerOw/issues) [MIT 许可证](LICENSE)
