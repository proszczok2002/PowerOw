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
    private StartupDiscovery? discovery;
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
        (text["Settings"], () => _ = window.Settings()),
        (text["TrayUpdate"], () =>
        {
            if (window.ReadyForUpdate)
                _ = update.CheckNow(window.Visible ? window : null);
        }),
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
                int blocked = 0;
                bool? guardPaused = null;
                Problem? problem = null;
                try
                {
                    blocked = engine.GuardAccount();
                    guardPaused = engine.ReadPaused();
                }
                catch (Exception error) when (Faults.Expected(error)) { problem = Faults.From(error); }

                IReadOnlyList<StartupEntry> added = [];
                if (guardPaused == true)
                    discovery = null;
                else if (guardPaused == false && (scan || discovery is null))
                {
                    try
                    {
                        var startup = engine.ScanStartupApplications();
                        discovery ??= new StartupDiscovery();
                        added = discovery.Observe(startup);
                        engine.RecordDiscoveries(added);
                    }
                    catch (Exception error) when (Faults.Expected(error)) { problem ??= Faults.From(error); }
                }
                return (blocked, guardPaused, added, problem);
            });
            if (allowClose)
                return;
            if (result.guardPaused is bool value)
            {
                paused = value;
                update.ProtectionChanged(!paused);
                _ = update.Tick();
            }
            var notices = new List<string>(2);
            if (result.added.Count == 1)
                notices.Add(text.Format("NewSingleNotice", result.added[0].Name));
            else if (result.added.Count > 1)
                notices.Add(text.Format("NewNotice", result.added.Count, string.Join(", ", result.added.Take(4).Select(entry => entry.Name))));
            if (result.blocked > 0)
                notices.Add(text.Format("BlockedNotice", result.blocked));
            if (notices.Count > 0)
            {
                tray?.Notify("PowerOw", string.Join(Environment.NewLine, notices), sound: result.added.Count > 0);
                if (window.Visible)
                    await window.RefreshAsync(announce: false);
            }
            else if (scan && window.Visible && result.problem is { } problem)
                tray?.Notify("PowerOw", text["Problem." + problem]);
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
