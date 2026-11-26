namespace PowerOw;

internal sealed record SettingsChoice(Preferences Appearance, bool Logon, bool Protection);

internal static class Dialogs
{
    internal static Button Button(string text, EventHandler? handler = null)
    {
        var button = new Button { Text = text, AccessibleName = TextCatalog.Plain(text), AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(104, 32), Padding = new Padding(8, 0, 8, 0) };
        if (handler is not null)
            button.Click += handler;
        return button;
    }

    internal static TextBox ReadingBox(string label, string value = "") => new()
    {
        AccessibleName = label, Text = value, ReadOnly = true, Multiline = true, WordWrap = true,
        ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, ShortcutsEnabled = true
    };

    internal static ComboBox Choices(string label, IEnumerable<string> items)
    {
        var result = new ComboBox { AccessibleName = label, DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        result.Items.AddRange(items.Cast<object>().ToArray());
        result.SelectedIndex = 0;
        return result;
    }

    internal static SettingsChoice? Settings(Form? owner, TextCatalog text, Preferences current, bool logon, bool protection,
        Func<Form, Task> checkUpdate)
    {
        using var form = new Dialog(text["SettingsTitle"], current.Theme, new Size(575, 460))
        {
            StartPosition = owner is null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
            ShowInTaskbar = owner is null
        };
        var language = Choices(text["Language"], new[] { text["WindowsLanguage"] }.Concat(TextCatalog.Languages));
        var themes = Choices(text["Theme"], [text["ThemeSystem"], text["ThemeLight"], text["ThemeDark"]]);
        var autoStart = new SpokenCheckBox { Text = text["StartWithWindows"], AccessibleName = text["StartWithWindows"],
            AutoSize = true, Checked = logon, Margin = new Padding(3, 12, 3, 3) };
        var guard = new SpokenCheckBox { Text = text["EnableProtection"], AccessibleName = text["EnableProtection"],
            AutoSize = true, Checked = protection };
        language.SelectedIndex = Array.IndexOf(TextCatalog.Codes, current.Language) + 1;
        themes.SelectedIndex = (int)current.Theme;
        form.Body.Controls.Add(new Label { Text = text["Language"], AutoSize = true }, 0, 0);
        form.Body.Controls.Add(language, 0, 1);
        form.Body.Controls.Add(new Label { Text = text["Theme"], AutoSize = true }, 0, 2);
        form.Body.Controls.Add(themes, 0, 3);
        form.Body.Controls.Add(autoStart, 0, 4);
        form.Body.Controls.Add(guard, 0, 5);
        var automatic = new SpokenCheckBox
        {
            Text = text["UpdatesAutomatic"], AccessibleName = text["UpdatesAutomatic"], AutoSize = true,
            Checked = current.AutomaticUpdates, Margin = new Padding(3, 12, 3, 3)
        };
        var interval = Choices(text["UpdateInterval"], Preferences.UpdateIntervals.Select(minutes => text.Format("UpdateMinutes", minutes)));
        interval.SelectedIndex = Math.Max(0, Array.IndexOf(Preferences.UpdateIntervals, current.UpdateIntervalMinutes));
        interval.Enabled = automatic.Checked;
        automatic.CheckedChanged += (_, _) => interval.Enabled = automatic.Checked;
        var checkNow = Button(text["UpdateCheck"]);
        checkNow.Click += async (_, _) =>
        {
            checkNow.Enabled = false;
            try { await checkUpdate(form); }
            finally { if (!form.IsDisposed) checkNow.Enabled = true; }
        };
        form.Body.Controls.Add(automatic, 0, 6);
        form.Body.Controls.Add(new Label { Text = text["UpdateInterval"], AutoSize = true }, 0, 7);
        form.Body.Controls.Add(interval, 0, 8);
        form.Body.Controls.Add(checkNow, 0, 9);
        themes.SelectedIndexChanged += (_, _) => form.SetTheme((ThemeMode)themes.SelectedIndex);
        form.Buttons(text, text["Ok"]);
        form.Shown += (_, _) => language.Focus();
        return form.ShowDialog(owner) == DialogResult.OK
            ? new SettingsChoice(new Preferences(language.SelectedIndex == 0 ? "auto" : TextCatalog.Codes[language.SelectedIndex - 1],
                (ThemeMode)themes.SelectedIndex, automatic.Checked, Preferences.UpdateIntervals[interval.SelectedIndex]),
                autoStart.Checked, guard.Checked)
            : null;
    }

    internal static void Support(Form owner, TextCatalog text, ThemeMode theme, Action<string> open)
    {
        using var form = new Dialog(text["SupportTitle"], theme, new Size(480, 285));
        var rows = new (string Key, string Address)[]
        {
            ("SupportPayPal", "https://paypal.me/proszczok"),
            ("SupportRevolut", "https://revolut.me/r_rosiejka"),
            ("SupportProject", Updates.Repository),
            ("SupportIssues", Updates.Repository + "/issues"),
            ("SupportWebsite", "https://proszczok.pl/")
        };
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            form.Body.Controls.Add(Button(text[row.Key], (_, _) => open(row.Address)), 0, i);
        }
        form.Buttons(text, text["Close"], closeOnly: true);
        form.Shown += (_, _) => form.Body.Controls[0].Select();
        form.ShowDialog(owner);
    }

    internal static int? Delay(Form owner, TextCatalog text, ThemeMode theme)
    {
        using var form = new Dialog(text["DelayTitle"], theme, new Size(505, 220));
        var input = new NumericUpDown { Minimum = 5, Maximum = 600, Value = 30, AccessibleName = text["Seconds"],
            Width = 130, Dock = DockStyle.Left };
        form.Body.Controls.Add(new Label { Text = text["DelayExplanation"], MaximumSize = new Size(455, 0), AutoSize = true }, 0, 0);
        form.Body.Controls.Add(new Label { Text = text["Seconds"], AutoSize = true }, 0, 1);
        form.Body.Controls.Add(input, 0, 2);
        form.Buttons(text, text["Delay"]);
        form.Shown += (_, _) => input.Select();
        return form.ShowDialog(owner) == DialogResult.OK ? (int)input.Value : null;
    }

    internal static void ShowText(Form owner, TextCatalog text, ThemeMode theme, string title, string value)
    {
        using var form = new Dialog(title, theme, new Size(650, 420), resize: true);
        var details = ReadingBox(title, value);
        form.Body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        form.Body.Controls.Add(details, 0, 0);
        form.Buttons(text, text["Close"], closeOnly: true);
        form.Shown += (_, _) => { details.Focus(); details.SelectionStart = 0; };
        form.ShowDialog(owner);
    }

    internal sealed class Dialog : Form
    {
        private ThemeMode theme;
        private readonly FlowLayoutPanel actions = new() { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        internal TableLayoutPanel Body { get; } = new() { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true };

        internal Dialog(string title, ThemeMode theme, Size size, bool resize = false)
        {
            this.theme = theme;
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = resize ? FormBorderStyle.Sizable : FormBorderStyle.FixedDialog;
            ClientSize = size;
            MinimumSize = resize ? new Size(470, 300) : Size;
            MaximizeBox = resize;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), RowCount = 2, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(Body, 0, 0);
            layout.Controls.Add(actions, 0, 1);
            Controls.Add(layout);
            Load += (_, _) => Appearance.Apply(this, theme);
        }

        internal void AddAction(Button action) => actions.Controls.Add(action);

        internal void SetTheme(ThemeMode choice)
        {
            theme = choice;
            Appearance.Apply(this, theme);
        }

        internal void Buttons(TextCatalog text, string action, bool closeOnly = false)
        {
            var ok = Button(action);
            ok.DialogResult = DialogResult.OK;
            if (!closeOnly)
            {
                var cancel = Button(text["Cancel"]);
                cancel.DialogResult = DialogResult.Cancel;
                actions.Controls.Add(cancel);
                CancelButton = cancel;
            }
            else
                CancelButton = ok;
            actions.Controls.Add(ok);
            AcceptButton = ok;
        }
    }
}
