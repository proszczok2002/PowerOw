using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms.Automation;

namespace PowerOw;

internal sealed class MainForm : Form
{
    private readonly StartupEngine engine;
    private readonly MenuStrip menu = new();
    private readonly AnnouncedMenu context = new();
    private readonly Label heading = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 23) };
    private readonly Label summary = new() { AutoSize = true };
    private readonly Label typeLabel = new() { AutoSize = true };
    private readonly Label stateLabel = new() { AutoSize = true };
    private readonly Label searchLabel = new() { AutoSize = true };
    private readonly Label detailsLabel = new() { AutoSize = true };
    private readonly Label status = new() { AutoSize = true, AccessibleRole = AccessibleRole.StatusBar };
    private readonly PictureBox mark = new() { Size = new Size(44, 44), SizeMode = PictureBoxSizeMode.Zoom, TabStop = false };
    private readonly ComboBox source = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox search = new() { Dock = DockStyle.Fill };
    private readonly ListBox entries = new()
    {
        Dock = DockStyle.Fill, IntegralHeight = false, SelectionMode = SelectionMode.MultiExtended,
        HorizontalScrollbar = true, BorderStyle = BorderStyle.FixedSingle
    };
    private readonly TextBox details = Dialogs.ReadingBox("");
    private readonly FlowLayoutPanel editBar = new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
    private readonly Dictionary<string, ToolStripMenuItem> top = new();
    private readonly Dictionary<Change, Button> buttons = new();
    private readonly Dictionary<Change, List<ToolStripMenuItem>> commandItems = new();
    private readonly Dictionary<string, ToolStripMenuItem> namedItems = new();
    private readonly Button reload = Dialogs.Button("");
    private readonly Button prefs = Dialogs.Button("");
    private readonly Button history = Dialogs.Button("");
    private readonly Button pause = Dialogs.Button("");
    private readonly Button support = Dialogs.Button("");
    private Preferences preferences;
    private TextCatalog text;
    private readonly Icon appIcon;
    private Inventory? inventory;
    private bool busy;
    private bool filling;
    private bool pendingReload;

    internal event Action<Preferences>? PreferencesChanged;
    internal event Action? PauseChanged;
    internal event Action? ExitRequested;
    internal event Func<Form, Task>? CheckUpdateRequested;
    internal bool Paused => inventory?.State.Paused ?? false;
    internal bool ReadyForUpdate => !busy && !Application.OpenForms.Cast<Form>().Any(form => form != this && form.Visible && form.Modal);

    internal MainForm(StartupEngine engine, Preferences preferences)
    {
        this.engine = engine;
        this.preferences = preferences;
        text = new TextCatalog(preferences.Language);
        appIcon = Appearance.Icon();
        Icon = appIcon;
        mark.Image = appIcon.ToBitmap();
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(735, 570);
        ClientSize = new Size(1000, 650);
        KeyPreview = true;
        BuildMenu();
        BuildLayout();
        Localize();
        source.SelectedIndexChanged += (_, _) => ApplyFilter();
        filter.SelectedIndexChanged += (_, _) => ApplyFilter();
        search.TextChanged += (_, _) => ApplyFilter();
        entries.SelectedIndexChanged += (_, _) => UpdateSelection();
        entries.ContextMenuStrip = context;
        entries.KeyDown += EntryKeyDown;
        reload.Click += async (_, _) => await RefreshAsync();
        history.Click += (_, _) => ShowHistory();
        prefs.Click += async (_, _) => await Settings();
        pause.Click += async (_, _) => await TogglePause();
        support.Click += (_, _) => ShowSupport();
        Load += (_, _) =>
        {
            FitWorkingArea();
            Appearance.Apply(this, preferences.Theme);
        };
        Shown += async (_, _) =>
        {
            entries.Focus();
            await RefreshAsync(announce: false);
        };
    }

    private void FitWorkingArea()
    {
        Rectangle area = Screen.FromControl(this).WorkingArea;
        int width = Math.Min(Width, area.Width);
        int height = Math.Min(Height, area.Height);
        MinimumSize = new Size(Math.Min(MinimumSize.Width, width), Math.Min(MinimumSize.Height, height));
        if (width != Width || height != Height || !area.Contains(Bounds))
            Bounds = new Rectangle(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
    }

    private void BuildLayout()
    {
        var page = new TableLayoutPanel { ColumnCount = 1, RowCount = 8, Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 10) };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 69));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 31));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var banner = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 10) };
        banner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        banner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        banner.Controls.Add(mark, 0, 0);
        banner.SetRowSpan(mark, 2);
        mark.Margin = new Padding(0, 4, 10, 0);
        banner.Controls.Add(heading, 1, 0);
        banner.Controls.Add(summary, 1, 1);
        page.Controls.Add(banner, 0, 0);
        var searchBar = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 7) };
        searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 49));
        searchBar.Controls.Add(typeLabel, 0, 0);
        searchBar.Controls.Add(stateLabel, 1, 0);
        searchBar.Controls.Add(searchLabel, 2, 0);
        searchBar.Controls.Add(source, 0, 1);
        searchBar.Controls.Add(filter, 1, 1);
        searchBar.Controls.Add(search, 2, 1);
        source.TabIndex = 0;
        filter.TabIndex = 1;
        search.TabIndex = 2;
        page.Controls.Add(searchBar, 0, 1);
        page.Controls.Add(entries, 0, 2);
        page.Controls.Add(editBar, 0, 3);
        foreach (Change command in new[] { Change.Disable, Change.Restore, Change.Remove, Change.DisableAndGuard })
        {
            var button = Dialogs.Button("");
            button.Click += async (_, _) => await Run(command);
            buttons.Add(command, button);
            editBar.Controls.Add(button);
        }
        page.Controls.Add(detailsLabel, 0, 4);
        page.Controls.Add(details, 0, 5);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 6, 0, 4) };
        controls.Controls.AddRange([reload, history, pause, prefs, support]);
        page.Controls.Add(controls, 0, 6);
        page.Controls.Add(status, 0, 7);
        Controls.Add(page);
        Controls.Add(menu);
        MainMenuStrip = menu;
    }

    private void BuildMenu()
    {
        foreach (string name in new[] { "MenuApp", "MenuEntry", "MenuView", "MenuHelp" })
        {
            var item = new ToolStripMenuItem();
            top.Add(name, item);
            menu.Items.Add(item);
        }
        var app = top["MenuApp"].DropDownItems;
        Add(app, "Settings", async () => await Settings(), Keys.Control | Keys.Oemcomma);
        Add(app, "WindowsSettings", () => Launch("ms-settings:startupapps"));
        app.Add(new ToolStripSeparator());
        Add(app, "Exit", () => ExitRequested?.Invoke());
        var entry = top["MenuEntry"].DropDownItems;
        foreach (Change command in new[] { Change.Disable, Change.Restore, Change.Enable, Change.Remove, Change.DisableAndGuard,
                     Change.RemoveAndGuard, Change.StopGuarding, Change.Delay, Change.ForgetBackup })
        {
            var item = new ToolStripMenuItem();
            var other = new ToolStripMenuItem();
            item.Click += async (_, _) => await Run(command);
            other.Click += async (_, _) => await Run(command);
            item.ShortcutKeyDisplayString = command switch
            {
                Change.Disable => "Ctrl+D", Change.Restore => "Ctrl+E", Change.Remove => "Delete",
                Change.DisableAndGuard => "Ctrl+G", Change.RemoveAndGuard => "Ctrl+Shift+G", _ => ""
            };
            entry.Add(item);
            context.Items.Add(other);
            commandItems.Add(command, [item, other]);
        }
        entry.Add(new ToolStripSeparator());
        context.Items.Add(new ToolStripSeparator());
        Add(entry, "Inspect", async () => await Inspect(), Keys.Control | Keys.I);
        Add(entry, "CopyDetails", CopyDetails, Keys.Control | Keys.Shift | Keys.C);
        Add(entry, "OpenLocation", OpenLocation);
        var inspectContext = new ToolStripMenuItem();
        var copyContext = new ToolStripMenuItem();
        var openContext = new ToolStripMenuItem();
        inspectContext.Click += async (_, _) => await Inspect();
        copyContext.Click += (_, _) => CopyDetails();
        openContext.Click += (_, _) => OpenLocation();
        context.Items.AddRange([inspectContext, copyContext, openContext]);
        namedItems["ContextInspect"] = inspectContext;
        namedItems["ContextCopyDetails"] = copyContext;
        namedItems["ContextOpenLocation"] = openContext;
        var view = top["MenuView"].DropDownItems;
        Add(view, "Refresh", async () => await RefreshAsync(), Keys.F5);
        Add(view, "FocusSearch", () => { search.Focus(); search.SelectAll(); }, Keys.Control | Keys.F);
        Add(view, "FocusDetails", () => details.Focus(), Keys.F6);
        Add(view, "SelectAll", SelectVisible, Keys.Control | Keys.A, setShortcut: false);
        Add(view, "History", ShowHistory, Keys.Control | Keys.H);
        Add(view, "Pause", async () => await TogglePause());
        Add(top["MenuHelp"].DropDownItems, "Keyboard", ShowHelp, Keys.F1);
        Add(top["MenuHelp"].DropDownItems, "UpdateCheck", async () => await CheckUpdate(this));
        Add(top["MenuHelp"].DropDownItems, "SupportTitle", ShowSupport);

        void Add(ToolStripItemCollection target, string key, Action action, Keys shortcut = Keys.None, bool setShortcut = true)
        {
            var item = new ToolStripMenuItem();
            if (setShortcut)
                item.ShortcutKeys = shortcut;
            else
                item.ShortcutKeyDisplayString = "Ctrl+A";
            item.Click += (_, _) => action();
            target.Add(item);
            namedItems.Add(key, item);
        }
    }

    private void Localize()
    {
        filling = true;
        try
        {
            Text = Updates.WindowTitle;
            heading.Text = "PowerOw";
            summary.Text = text["AdminSummary"];
            typeLabel.Text = source.AccessibleName = text["Source"];
            stateLabel.Text = filter.AccessibleName = text["State"];
            searchLabel.Text = search.AccessibleName = text["Search"];
            entries.AccessibleName = text["Entries"];
            detailsLabel.Text = details.AccessibleName = text["Details"];
            int previousType = Math.Max(0, source.SelectedIndex);
            int previousFilter = Math.Max(0, filter.SelectedIndex);
            source.Items.Clear();
            source.Items.AddRange(new[] { "StartupPrograms", "Logon", "ProgramTasks", "ProgramServices", "Kind.Packaged" }
                .Select(key => (object)text[key]).ToArray());
            source.SelectedIndex = previousType;
            filter.Items.Clear();
            filter.Items.AddRange(new[] { "AllStates", "FilterActive", "FilterInactive", "FilterGuarded", "FilterReadOnly" }
                .Select(key => (object)text[key]).ToArray());
            filter.SelectedIndex = previousFilter;
            foreach (var pair in top)
                pair.Value.Text = text[pair.Key];
            foreach (var pair in namedItems)
            {
                string ownKey = pair.Key.StartsWith("Context", StringComparison.Ordinal) ? pair.Key[7..] : pair.Key;
                pair.Value.Text = text[ownKey];
            }
            foreach (var pair in commandItems)
                foreach (var item in pair.Value)
                    item.Text = text[pair.Key.ToString()];
            foreach (var pair in buttons)
                SetButton(pair.Value, text[pair.Key.ToString()]);
            SetButton(reload, text["Refresh"]);
            SetButton(history, text["History"]);
            SetButton(prefs, text["Settings"]);
            SetButton(support, text["SupportTitle"]);
            context.AccessibleName = text["EntryMenu"];
            SetPauseCaption();
        }
        finally { filling = false; }
        Appearance.Apply(this, preferences.Theme);
    }

    private static void SetButton(Button button, string label)
    {
        button.Text = label;
        button.AccessibleName = TextCatalog.Plain(label);
    }

    internal async Task RefreshAsync(bool announce = true)
    {
        if (busy)
        {
            pendingReload = true;
            return;
        }
        SetBusy(true);
        if (announce)
            SetStatus(text["Scanning"], announce: false);
        try { await LoadInventory(); }
        catch (Exception error) when (Faults.Expected(error)) { ShowProblem(error); }
        finally { SetBusy(false); }
    }

    private async Task LoadInventory()
    {
        var next = await Task.Run(engine.Load);
        if (IsDisposed)
            return;
        inventory = next;
        ApplyFilter();
        SetPauseCaption();
    }

    private void ApplyFilter()
    {
        if (filling || inventory is null || IsDisposed)
            return;
        var keys = Selection().Select(item => item.Entry.Id.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int oldIndex = entries.SelectedIndex;
        int previousTop = entries.Items.Count > 0 ? entries.TopIndex : 0;
        int group = source.SelectedIndex;
        int state = filter.SelectedIndex;
        string term = search.Text.Trim();
        var visible = inventory.Items.Where(row =>
            InGroup(row.Entry, group) && InState(row, state) && Matches(row, term))
            .OrderBy(row => row.Entry.Name, StringComparer.Create(text.Culture, ignoreCase: true))
            .ThenBy(row => row.Entry.Location, StringComparer.OrdinalIgnoreCase)
            .Select(row => new ListRow(row, RowText(row))).ToArray();
        filling = true;
        entries.BeginUpdate();
        try
        {
            entries.Items.Clear();
            entries.Items.AddRange(visible);
            for (int i = 0; i < visible.Length; i++)
                if (keys.Contains(visible[i].Value.Entry.Id.Key))
                    entries.SetSelected(i, true);
            if (entries.SelectedIndex < 0 && visible.Length > 0)
                entries.SelectedIndex = Math.Clamp(oldIndex, 0, visible.Length - 1);
            if (keys.Count > 0 && visible.Length > 0 && entries.SelectedIndex >= previousTop)
                entries.TopIndex = Math.Min(previousTop, visible.Length - 1);
            entries.HorizontalExtent = visible.Length == 0 ? 0 :
                visible.Max(row => TextRenderer.MeasureText(row.Text, entries.Font).Width) + 12;
        }
        finally
        {
            entries.EndUpdate();
            filling = false;
        }
        UpdateSelection();
        SetStatus(InventoryStatus(visible.Length), announce: false);
    }

    private static bool InGroup(StartupEntry entry, int group)
    {
        StartupKind kind = entry.Id.Kind;
        return group switch
        {
            1 => kind is StartupKind.Registry or StartupKind.StartupFolder,
            2 => kind == StartupKind.Task,
            3 => kind == StartupKind.Service,
            4 => kind == StartupKind.Packaged,
            _ => kind is StartupKind.Registry or StartupKind.StartupFolder or StartupKind.Packaged ||
                (kind == StartupKind.Task && (entry.Extra.Contains("LogonTrigger", StringComparison.Ordinal) ||
                                              entry.Extra.Contains("BootTrigger", StringComparison.Ordinal)))
        };
    }

    private static bool InState(ViewEntry row, int state) => state switch
    {
        1 => row.Entry.Active,
        2 => !row.Entry.Active && row.Entry.State != EntryState.ReadOnly,
        3 => row.Saved?.Guard == true,
        4 => !row.CanWrite,
        _ => true
    };

    private bool Matches(ViewEntry row, string term) => term.Length == 0 ||
        new[] { row.Entry.Name, row.Entry.Command, row.Entry.Location, Kind(row.Entry), StateName(row) }
            .Any(value => text.Culture.CompareInfo.IndexOf(value, term,
                System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0);

    private string Kind(StartupEntry entry) => entry.Id.Kind == StartupKind.System
        ? text["System." + entry.Id.Site] : text["Kind." + entry.Id.Kind];

    private string StateName(ViewEntry row) =>
        text["EntryState." + row.Entry.State] +
        (row.Saved?.Guard == true ? " · " + text["Guarded"] : "") +
        (row.Saved?.Pending == true ? " · " + text["Pending"] : "");

    private string RowText(ViewEntry row)
    {
        string name = row.Entry.Name.Replace('\r', ' ').Replace('\n', ' ');
        return $"{name}  •  {StateName(row)}  •  {Kind(row.Entry)}  •  {text[row.Entry.Id.Scope.ToString()]}";
    }

    private string InventoryStatus(int count)
    {
        var result = new List<string> { text.Format("ResultCount", count) };
        if (inventory is { StateProblem: { } problem })
            result.Add(text["Problem." + problem]);
        else
            result.Add(text[Paused ? "ProtectionPaused" : "ProtectionOn"]);
        if (engine.Scope == StartupScope.Machine)
            result.Add(text[Maintenance.GuardRunning() ? "ServiceRunning" : "ServiceStopped"]);
        if (inventory is { Unavailable.Count: > 0 } data)
            result.Add(text.Format("SomeUnavailable", string.Join(", ", data.Unavailable.Select(kind => text["Kind." + kind]))));
        return string.Join("    ·    ", result);
    }

    private void SetStatus(string value, bool announce, string? spoken = null)
    {
        if (IsDisposed || status.Text == value)
            return;
        status.Text = value;
        status.MaximumSize = new Size(Math.Max(280, ClientSize.Width - 40), 0);
        if (announce && status.IsHandleCreated && Visible && ContainsFocus)
            _ = status.AccessibilityObject.RaiseAutomationNotification(AutomationNotificationKind.Other,
                AutomationNotificationProcessing.MostRecent, spoken ?? value);
    }

    private void SetBusy(bool value)
    {
        busy = value;
        UpdateSelection();
        if (!value && pendingReload && IsHandleCreated && !IsDisposed)
        {
            pendingReload = false;
            BeginInvoke(async () => await RefreshAsync(announce: false));
        }
    }

    private ViewEntry[] Selection() => entries.SelectedItems.Cast<ListRow>().Select(row => row.Value).ToArray();

    private void UpdateSelection()
    {
        if (filling)
            return;
        var selected = Selection();
        string value = selected.Length switch
        {
            0 => text["NoSelection"],
            1 => DetailText(selected[0]),
            _ => text.Format("SelectedCount", selected.Length) + Environment.NewLine +
                string.Join(Environment.NewLine, selected.Select(row => row.Entry.Name))
        };
        if (!string.Equals(details.Text, value, StringComparison.Ordinal))
        {
            details.Text = value;
            details.SelectionStart = 0;
        }
        foreach (var pair in commandItems)
        {
            bool enabled = !busy && selected.Length > 0 && selected.All(row => Allows(row, pair.Key)) &&
                (pair.Key != Change.Delay || selected.Length == 1);
            foreach (var item in pair.Value)
                item.Enabled = enabled;
            if (buttons.TryGetValue(pair.Key, out var button))
                button.Enabled = pair.Key == Change.Restore && !enabled ? !busy && selected.Length > 0 &&
                    selected.All(row => Allows(row, Change.Enable)) : enabled;
        }
        namedItems["Inspect"].Enabled = namedItems["ContextInspect"].Enabled = selected.Length == 1 && !busy;
        namedItems["OpenLocation"].Enabled = namedItems["ContextOpenLocation"].Enabled = selected.Length == 1;
        namedItems["CopyDetails"].Enabled = namedItems["ContextCopyDetails"].Enabled = selected.Length > 0;
        pause.Enabled = namedItems["Pause"].Enabled = !busy && inventory is { StateProblem: null };
        reload.Enabled = namedItems["Refresh"].Enabled = !busy;
    }

    private string DetailText(ViewEntry row)
    {
        var entry = row.Entry;
        var lines = new List<string>
        {
            entry.Name + "  •  " + StateName(row),
            Kind(entry) + "  •  " + text[entry.Id.Scope.ToString()],
            text["Location"], entry.Location,
            text["Command"], entry.Command.Length > 0 ? entry.Command : text["NoData"]
        };
        if (entry.Target is not null)
            lines.AddRange([text["File"], entry.Target]);
        if (entry.Extra.Length > 0)
            lines.AddRange([text["SourceDetails"], TranslateExtra(entry)]);
        if (row.Saved is { Mode: SavedMode.Delayed } saved)
            lines.Add(text.Format("DelayApplied", saved.Original.DelaySeconds));
        if (row.Saved?.Guard == true)
            lines.Add(text["RuleIdentity"]);
        if (!row.CanWrite)
            lines.Add(text[entry.Id.Kind == StartupKind.Packaged ? "PackagedExplanation" : "ReadOnlyExplanation"]);
        return string.Join(Environment.NewLine, lines);
    }

    private string TranslateExtra(StartupEntry entry) => entry.Id.Kind == StartupKind.Task
        ? string.Join(", ", entry.Extra.Split(", ").Select(trigger => text["Trigger." + trigger] == "Trigger." + trigger
            ? trigger : text["Trigger." + trigger]))
        : entry.Extra;

    private static bool Allows(ViewEntry row, Change change)
    {
        if (!row.CanWrite)
            return false;
        var cap = row.Entry.Actions;
        return change switch
        {
            Change.Restore => row.Saved is not null,
            Change.Enable => row.Saved is null && row.Entry.State == EntryState.WindowsDisabled && cap.HasFlag(EntryActions.Enable),
            Change.StopGuarding => row.Saved?.Guard == true,
            Change.ForgetBackup => row.Saved is { Guard: false, Mode: not SavedMode.Delayed },
            Change.Delay => row.Saved is null && row.Entry.State == EntryState.Active && cap.HasFlag(EntryActions.Delay),
            Change.Disable => row.Saved is null && row.Entry.Active && cap.HasFlag(EntryActions.Disable),
            Change.Remove => row.Saved is not { Mode: SavedMode.Delayed or SavedMode.Removed } && cap.HasFlag(EntryActions.Remove),
            Change.DisableAndGuard => row.Saved is not { Guard: true } && row.Saved?.Mode != SavedMode.Removed &&
                cap.HasFlag(EntryActions.Disable) && (row.Saved is not null || row.Entry.Active || row.Entry.Id.Kind != StartupKind.Service),
            Change.RemoveAndGuard => row.Saved is not { Guard: true } && row.Saved?.Mode != SavedMode.Delayed && cap.HasFlag(EntryActions.Remove),
            _ => false
        };
    }

    private async Task Run(Change action)
    {
        if (busy || IsDisposed)
            return;
        var selected = Selection();
        if (action == Change.Restore && selected.Length > 0 && selected.All(row => Allows(row, Change.Enable)))
            action = Change.Enable;
        if (selected.Length == 0 || !selected.All(row => Allows(row, action)))
            return;
        int seconds = 0;
        if (action == Change.Delay)
        {
            if (selected.Length != 1 || Dialogs.Delay(this, text, preferences.Theme) is not int delay)
                return;
            seconds = delay;
        }
        else if (action is not (Change.Enable or Change.StopGuarding) && !Confirm(action, selected))
            return;
        SetBusy(true);
        var failures = new List<string>();
        try
        {
            await Task.Run(() =>
            {
                foreach (var entry in selected)
                {
                    try { engine.Apply(entry, action, seconds); }
                    catch (Exception error) when (Faults.Expected(error))
                    {
                        failures.Add(entry.Entry.Name + " — " + text["Problem." + Faults.From(error)]);
                    }
                }
            });
            await LoadInventory();
            if (failures.Count == 0)
            {
                string message = text.Format("ChangedCount", selected.Length);
                SetStatus(message + "    ·    " + InventoryStatus(entries.Items.Count), true, message);
            }
            else
                Dialogs.ShowText(this, text, preferences.Theme, text["Errors"], string.Join(Environment.NewLine, failures));
        }
        catch (Exception error) when (Faults.Expected(error)) { ShowProblem(error); }
        finally
        {
            SetBusy(false);
            if (Visible)
                entries.Focus();
        }
    }

    private bool Confirm(Change action, ViewEntry[] selected)
    {
        string key = action switch
        {
            Change.Remove => "ConfirmRemove",
            Change.RemoveAndGuard => "ConfirmRemoveGuard",
            Change.Disable => "ConfirmDisable",
            Change.DisableAndGuard => "ConfirmDisableGuard",
            Change.Restore => "ConfirmRestore",
            Change.ForgetBackup => "ConfirmForget",
            _ => "ConfirmDisable"
        };
        string names = string.Join(Environment.NewLine, selected.Take(6).Select(row => row.Entry.Name));
        string message = text.Format(key, selected.Length) + Environment.NewLine + Environment.NewLine + names;
        return MessageBox.Show(this, message, TextCatalog.Plain(text[action.ToString()]),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    internal async Task TogglePause()
    {
        if (busy)
            return;
        SetBusy(true);
        try
        {
            await Task.Run(() => engine.SetPaused(!engine.ReadPaused()));
            await LoadInventory();
            PauseChanged?.Invoke();
        }
        catch (Exception error) when (Faults.Expected(error)) { ShowProblem(error); }
        finally { SetBusy(false); }
    }

    private void SetPauseCaption()
    {
        string label = text[Paused ? "Resume" : "Pause"];
        SetButton(pause, label);
        namedItems["Pause"].Text = label;
    }

    internal async Task Settings()
    {
        if (!ReadyForUpdate)
            return;
        SetBusy(true);
        try
        {
            var (logon, paused) = await Task.Run(() => (Tasks.LogonEnabled(), engine.ReadPaused()));
            if (Dialogs.Settings(Visible ? this : null, text, preferences, logon, !paused, CheckUpdate) is not { } chosen)
                return;
            await Task.Run(() =>
            {
                if (chosen.Logon != logon)
                    Tasks.SetLogonEnabled(chosen.Logon);
                try
                {
                    if (chosen.Protection == paused)
                        engine.SetPaused(!chosen.Protection);
                    if (AccountAccess.Available && chosen.Appearance != preferences)
                        AccountAccess.Run(() => { chosen.Appearance.Save(); return true; });
                }
                catch
                {
                    if (chosen.Logon != logon)
                        Tasks.SetLogonEnabled(logon);
                    if (chosen.Protection == paused && engine.ReadPaused() != paused)
                        engine.SetPaused(paused);
                    throw;
                }
            });
            preferences = chosen.Appearance;
            text = new TextCatalog(preferences.Language);
            Appearance.SetNative(preferences.Theme);
            Localize();
            await LoadInventory();
            PreferencesChanged?.Invoke(preferences);
            PauseChanged?.Invoke();
        }
        catch (Exception error) when (Faults.Expected(error)) { ShowProblem(error); }
        finally { SetBusy(false); }
    }

    private void ShowHistory()
    {
        var events = inventory?.State.History;
        string content = events is not { Count: > 0 } ? text["EmptyHistory"] : string.Join(Environment.NewLine + Environment.NewLine,
            events.Select(eventItem => eventItem.When.ToLocalTime().ToString("g", text.Culture) + "  •  " +
                text["Activity." + eventItem.Kind] + "  •  " + eventItem.Entry + Environment.NewLine + eventItem.Location +
                (eventItem.Problem is { } problem ? Environment.NewLine + text["Problem." + problem] : "")));
        Dialogs.ShowText(this, text, preferences.Theme, TextCatalog.Plain(text["History"]), content);
    }

    private void ShowHelp() => Dialogs.ShowText(this, text, preferences.Theme, TextCatalog.Plain(text["Keyboard"]), text["KeyboardHelp"]);

    private Task CheckUpdate(Form owner) => CheckUpdateRequested?.Invoke(owner) ?? Task.CompletedTask;

    private void ShowSupport() => Dialogs.Support(this, text, preferences.Theme, Launch);

    private void CopyDetails()
    {
        try
        {
            if (details.TextLength > 0 && entries.SelectedIndices.Count > 0)
                Clipboard.SetText(details.Text);
        }
        catch (ExternalException) { ShowProblem(new StartupException(Problem.Unavailable)); }
    }

    private void OpenLocation()
    {
        var selected = Selection();
        if (selected.Length != 1)
            return;
        StartupEntry entry = selected[0].Entry;
        if (entry.Id.Kind == StartupKind.Packaged)
            Launch("ms-settings:startupapps");
        else if (entry.Id.Kind == StartupKind.Task)
            Launch(Path.Combine(Environment.SystemDirectory, "taskschd.msc"));
        else if (entry.Id.Kind is StartupKind.Service or StartupKind.Driver)
            Launch(Path.Combine(Environment.SystemDirectory, "services.msc"));
        else if (entry.Id.Kind == StartupKind.StartupFolder || CommandPath.IsLocalPath(entry.Target))
        {
            string file = entry.Id.Kind == StartupKind.StartupFolder ? entry.Location : entry.Target!;
            if (Path.GetDirectoryName(file) is { } folder && Directory.Exists(folder))
                Launch(folder);
            else
                ShowProblem(new StartupException(Problem.BadTarget));
        }
        else
            Dialogs.ShowText(this, text, preferences.Theme, text["Location"], entry.Location);
    }

    private void Launch(string path)
    {
        try { using var launched = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception error) when (Faults.Expected(error)) { ShowProblem(error); }
    }

    private async Task Inspect()
    {
        var selected = Selection();
        if (selected.Length != 1 || busy)
            return;
        SetBusy(true);
        try
        {
            FileInspection result = await Task.Run(() => FileInspector.Inspect(selected[0].Entry.Target));
            var lines = new List<string> { selected[0].Entry.Target ?? "", text[result.Status] };
            if (result.Publisher.Length > 0)
                lines.Add(text["Publisher"] + Environment.NewLine + result.Publisher);
            if (result.Product.Length > 0 || result.Version.Length > 0)
                lines.Add(result.Product + " " + result.Version);
            if (result.Hash.Length > 0)
                lines.Add("SHA-256" + Environment.NewLine + result.Hash);
            Dialogs.ShowText(this, text, preferences.Theme, TextCatalog.Plain(text["Inspect"]), string.Join(Environment.NewLine, lines));
        }
        catch (Exception error) when (Faults.Expected(error)) { ShowProblem(error); }
        finally { SetBusy(false); }
    }

    internal void ShowProblem(Exception error) =>
        MessageBox.Show(Visible ? this : null, text["Problem." + Faults.From(error)], text["Errors"], MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void EntryKeyDown(object? sender, KeyEventArgs e)
    {
        var action = e.KeyData switch
        {
            Keys.Delete => Change.Remove,
            Keys.Control | Keys.D => Change.Disable,
            Keys.Control | Keys.E => Change.Restore,
            Keys.Control | Keys.G => Change.DisableAndGuard,
            Keys.Control | Keys.Shift | Keys.G => Change.RemoveAndGuard,
            _ => (Change?)null
        };
        if (action is Change command)
        {
            e.SuppressKeyPress = true;
            _ = Run(command);
        }
        else if (e.KeyData == (Keys.Control | Keys.A))
        {
            e.SuppressKeyPress = true;
            SelectVisible();
        }
        else if (e.KeyData == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            details.Focus();
        }
    }

    private void SelectVisible()
    {
        filling = true;
        entries.BeginUpdate();
        try
        {
            for (int i = 0; i < entries.Items.Count; i++)
                entries.SetSelected(i, true);
        }
        finally
        {
            entries.EndUpdate();
            filling = false;
        }
        UpdateSelection();
    }

    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        if (IsHandleCreated && !IsDisposed)
        {
            Appearance.SetNative(preferences.Theme);
            Appearance.Apply(this, preferences.Theme);
        }
    }

    protected override void WndProc(ref Message message)
    {
        bool colorsChanged = message.Msg == 0x001A && message.LParam != IntPtr.Zero &&
            Marshal.PtrToStringUni(message.LParam) == "ImmersiveColorSet";
        base.WndProc(ref message);
        if (colorsChanged && preferences.Theme == ThemeMode.System)
            OnSystemColorsChanged(EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            mark.Image?.Dispose();
            appIcon.Dispose();
            context.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record ListRow(ViewEntry Value, string Text)
    {
        public override string ToString() => Text;
    }
}
