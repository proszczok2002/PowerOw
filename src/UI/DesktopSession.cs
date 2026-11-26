namespace PowerOw;

internal sealed class DesktopSession : ApplicationContext
{
    private readonly MainForm window;
    private readonly StartupEngine engine;
    private readonly UpdateSession update;
    private readonly TrayIcon? tray;
    private readonly System.Windows.Forms.Timer? timer;
    private readonly RegisteredWaitHandle activation;
    private readonly SynchronizationContext dispatcher;
    private TextCatalog text;
    private Dictionary<string, string>? knownEntries;
    private int nextScan;
    private bool paused;
    private bool allowClose;
    private bool ticking;

    internal DesktopSession(StartupEngine engine, Preferences preferences, bool startInTray, EventWaitHandle showEvent)
    {
        this.engine = engine;
        text = new TextCatalog(preferences.Language);
        window = new MainForm(engine, preferences);
        update = new UpdateSession(window, preferences, Updates.Public, (title, message) => tray?.Notify(title, message));
        window.CheckUpdateRequested += update.CheckNow;
        _ = Task.Run(UpdateInstall.ClearExpired);
        dispatcher = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        activation = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) =>
            dispatcher.Post(_ => ShowWindow(refresh: true), null), null, Timeout.Infinite, executeOnlyOnce: false);
        window.ExitRequested += ExitThread;
        window.FormClosing += (_, e) =>
        {
            if (!allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                window.Hide();
            }
        };
        window.FormClosed += (_, _) => ExitThread();
        tray = new TrayIcon { Open = () => ShowWindow(refresh: true), Menu = TrayMenu };
        tray.SetText(text["Tray"], text["TrayMenu"]);
        window.PreferencesChanged += chosen =>
        {
            text = new TextCatalog(chosen.Language);
            tray.SetText(text["Tray"], text["TrayMenu"]);
            update.Configure(chosen);
        };
        window.PauseChanged += () =>
        {
            paused = window.Paused;
            update.ProtectionChanged(!paused);
        };
        timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += async (_, _) => await Tick();
        timer.Start();
        if (!startInTray)
            ShowWindow();
        dispatcher.Post(async _ => await Tick(), null);
    }

    private IReadOnlyList<(string Label, Action Run)> TrayMenu() =>
    [
        (text["TrayOpen"], () => ShowWindow(refresh: true)),
        (text["Refresh"], () => ShowWindow(refresh: true)),
        (text[paused ? "Resume" : "Pause"], () => { ShowWindow(); _ = window.TogglePause(); }),
        (text["Exit"], ExitThread)
    ];

    private void ShowWindow(bool refresh = false)
    {
        if (allowClose || window.IsDisposed)
            return;
        bool firstShow = !window.IsHandleCreated;
        if (!window.Visible)
        {
            update.WindowOpened();
            window.Show();
        }
        if (window.WindowState == FormWindowState.Minimized)
            window.WindowState = FormWindowState.Normal;
        window.Activate();
        window.BringToFront();
        if (refresh && !firstShow)
            _ = window.RefreshAsync(announce: false);
    }

    private async Task Tick()
    {
        if (ticking || allowClose)
            return;
        ticking = true;
        bool scan = nextScan++ % 4 == 0;
        try
        {
            var result = await Task.Run(() =>
            {
                int blocked = engine.GuardAccount();
                var logon = scan ? StartupEngine.QuickLogonEntries() : null;
                return (blocked, logon, engine.ReadPaused());
            });
            if (allowClose)
                return;
            paused = result.Item3;
            update.ProtectionChanged(!paused);
            _ = update.Tick();
            if (result.blocked > 0)
            {
                tray?.Notify("PowerOw", text.Format("BlockedNotice", result.blocked));
                if (window.Visible)
                    await window.RefreshAsync(announce: false);
            }
            if (result.logon is not null)
            {
                var current = result.logon.DistinctBy(entry => entry.Id.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(entry => entry.Id.Key, entry => entry.Name, StringComparer.OrdinalIgnoreCase);
                if (knownEntries is not null)
                {
                    var added = current.Where(pair => !knownEntries.ContainsKey(pair.Key)).ToArray();
                    if (added.Length > 0)
                    {
                        tray?.Notify("PowerOw", text.Format("NewNotice", added.Length, string.Join(", ", added.Take(4).Select(pair => pair.Value))));
                        if (window.Visible)
                            await window.RefreshAsync(announce: false);
                    }
                }
                knownEntries = current;
            }
        }
        catch (Exception error) when (Faults.Expected(error))
        {
            if (!allowClose && scan && window.Visible)
                tray?.Notify("PowerOw", text["Problem." + Faults.From(error)]);
        }
        finally { ticking = false; }
    }

    protected override void ExitThreadCore()
    {
        if (allowClose)
            return;
        allowClose = true;
        update.Dispose();
        activation.Unregister(null);
        timer?.Stop();
        tray?.Dispose();
        if (!window.IsDisposed)
            window.Close();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer?.Dispose();
            update.Dispose();
            window.Dispose();
        }
        base.Dispose(disposing);
    }
}
