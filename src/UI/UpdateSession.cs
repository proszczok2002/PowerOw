using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms.Automation;

namespace PowerOw;

internal sealed class UpdateSession(MainForm window, Preferences preferences, Updates updates,
    Action<string, string> notify) : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private Preferences settings = preferences;
    private DateTimeOffset next;
    private Version? offered;
    private UpdateRelease? pending;
    private bool pendingForOpening;
    private bool opened;
    private bool protection;
    private bool working;
    private bool disposed;

    internal void WindowOpened() => opened = true;

    internal void Configure(Preferences value)
    {
        if (settings.AutomaticUpdates != value.AutomaticUpdates || settings.UpdateIntervalMinutes != value.UpdateIntervalMinutes)
            next = default;
        if (!value.AutomaticUpdates)
            pending = null;
        settings = value;
    }

    internal void ProtectionChanged(bool enabled)
    {
        if (enabled && !protection)
            next = default;
        protection = enabled;
    }

    internal static bool ShouldCheck(bool automatic, bool protection, bool opening, DateTimeOffset now, DateTimeOffset next) =>
        automatic && (protection ? now >= next : opening);

    internal async Task Tick()
    {
        if (disposed || working || !settings.AutomaticUpdates || !window.ReadyForUpdate)
            return;
        if (pending is not null)
        {
            var update = pending;
            pending = null;
            if (protection || (pendingForOpening && window.Visible))
            {
                working = true;
                try { Offer(update, window.Visible && (pendingForOpening || window.ContainsFocus) ? window : null); }
                finally { working = false; }
            }
            return;
        }
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!ShouldCheck(settings.AutomaticUpdates, protection, opened && window.Visible, now, next))
            return;
        bool foreground = opened && window.Visible;
        opened = false;
        next = now.AddMinutes(settings.UpdateIntervalMinutes);
        working = true;
        try
        {
            var update = await updates.Check(stop.Token);
            if (!disposed && settings.AutomaticUpdates && update is not null && update.Version != offered)
            {
                pending = update;
                pendingForOpening = foreground;
            }
        }
        catch (Exception error) when (Expected(error)) { }
        finally { working = false; }
    }

    internal async Task CheckNow(Form? owner)
    {
        var text = new TextCatalog(settings.Language);
        if (working)
        {
            Message(owner, text, "UpdateBusy");
            return;
        }
        working = true;
        try
        {
            var update = await updates.Check(stop.Token);
            if (disposed || owner?.IsDisposed == true)
                return;
            pending = null;
            next = DateTimeOffset.UtcNow.AddMinutes(settings.UpdateIntervalMinutes);
            if (update is null)
                Message(owner, text, "UpdateCurrent");
            else
                Offer(update, owner);
        }
        catch (Exception error) when (Expected(error))
        {
            if (!disposed && owner?.IsDisposed != true)
                Message(owner, text, "UpdateCheckFailed", MessageBoxIcon.Warning);
        }
        finally { working = false; }
    }

    private void Offer(UpdateRelease update, Form? owner)
    {
        offered = update.Version;
        var text = new TextCatalog(settings.Language);
        string notes = Updates.NotesFor(update, text.Culture.Name);
        var yes = new TaskDialogButton(text["UpdateYes"]);
        var no = new TaskDialogButton(text["UpdateNo"]);
        var page = new TaskDialogPage
        {
            Caption = text["UpdateTitle"],
            Heading = text.Format("UpdateAvailable", Updates.DisplayVersion(update.Version)),
            Text = text["UpdateQuestion"],
            Icon = TaskDialogIcon.Information,
            AllowCancel = true,
            DefaultButton = no,
            Expander = notes.Length == 0 ? null : new TaskDialogExpander(notes)
            {
                CollapsedButtonText = text["UpdateNotes"],
                ExpandedButtonText = text["UpdateHideNotes"]
            }
        };
        page.Buttons.Add(yes);
        page.Buttons.Add(no);
        if (owner is null)
        {
            page.Created += (_, _) =>
            {
                IntPtr handle = page.BoundDialog!.Handle;
                _ = SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 3);
                if (!SetForegroundWindow(handle))
                    notify(text["UpdateTitle"], text.Format("UpdateAttention", Updates.DisplayVersion(update.Version)));
            };
        }
        var answer = owner is null
            ? TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen)
            : TaskDialog.ShowDialog(owner, page, TaskDialogStartupLocation.CenterOwner);
        if (answer == yes && !disposed)
            RunUpdate(update, owner, text);
    }

    private void RunUpdate(UpdateRelease release, Form? owner, TextCatalog text)
    {
        using var form = new Dialogs.Dialog(text["UpdateProgressTitle"], settings.Theme, new Size(535, 170))
        {
            StartPosition = owner is null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
            ShowInTaskbar = owner is null
        };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        cancellation.CancelAfter(TimeSpan.FromMinutes(8));
        var bar = new ProgressBar { AccessibleName = text["UpdateProgressTitle"], Dock = DockStyle.Fill, TabStop = true };
        var status = new TextBox
        {
            AccessibleName = text["UpdateStatus"], ReadOnly = true, Dock = DockStyle.Fill, Multiline = true,
            Text = text.Format("UpdateDownloading", 0)
        };
        var cancel = Dialogs.Button(text["Cancel"], (_, _) => cancellation.Cancel());
        form.CancelButton = cancel;
        form.AddAction(cancel);
        form.Body.AutoScroll = false;
        form.Body.RowCount = 2;
        form.Body.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        form.Body.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        form.Body.Controls.Add(status, 0, 0);
        form.Body.Controls.Add(bar, 0, 1);
        string? result = null;
        bool finished = false;
        bool consent = false;
        form.FormClosing += (_, args) =>
        {
            if (finished || args.CloseReason != CloseReason.UserClosing)
                return;
            args.Cancel = true;
            if (!consent)
                cancellation.Cancel();
        };
        form.Shown += async (_, _) =>
        {
            status.Select();
            status.SelectionLength = 0;
            try
            {
                using var staging = new UpdateInstall();
                int spoken = 0;
                var progress = new Progress<int>(percent =>
                {
                    if (consent || finished || form.IsDisposed)
                        return;
                    bar.Value = percent;
                    status.Text = text.Format("UpdateDownloading", percent);
                    if (percent / 25 > spoken)
                    {
                        spoken = percent / 25;
                        Announce(status);
                    }
                });
                await updates.Download(release, staging.FilePath, progress, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                consent = true;
                status.Text = text["UpdateWaitingForWindows"];
                status.Focus();
                cancel.Enabled = false;
                bar.Style = ProgressBarStyle.Marquee;
                Announce(status);
                await Task.Yield();
                int exit = await staging.RequestConsent(release.Version, text.Culture.Name,
                    !window.Visible || window.WindowState == FormWindowState.Minimized);
                if (exit != 0)
                {
                    if (exit != 1223)
                        UpdateInstall.RecordFailure("Installer", exit);
                    result = exit == 1223 ? "UpdateCanceled" : "UpdateInstallFailed";
                }
            }
            catch (OperationCanceledException) { result = "UpdateCanceled"; }
            catch (Exception error) when (Expected(error))
            {
                string phase = consent && error is System.ComponentModel.Win32Exception win32 ? win32.Message :
                    consent ? "Start" : "Download";
                UpdateInstall.RecordFailure(phase,
                    error is System.ComponentModel.Win32Exception native ? native.NativeErrorCode : error.HResult);
                result = consent ? "UpdateInstallFailed" : "UpdateDownloadFailed";
            }
            finally
            {
                finished = true;
                if (!form.IsDisposed)
                    form.Close();
            }
        };
        if (owner is null)
            form.ShowDialog();
        else
            form.ShowDialog(owner);
        if (result is not null && !disposed)
            Message(owner, text, result, result == "UpdateCanceled" ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private static void Announce(TextBox status) =>
        _ = status.AccessibilityObject.RaiseAutomationNotification(AutomationNotificationKind.Other,
            AutomationNotificationProcessing.ImportantMostRecent, status.Text);

    private static void Message(Form? owner, TextCatalog text, string key, MessageBoxIcon icon = MessageBoxIcon.Information)
    {
        if (owner is null)
            MessageBox.Show(text[key], text["UpdateTitle"], MessageBoxButtons.OK, icon);
        else
            MessageBox.Show(owner, text[key], text["UpdateTitle"], MessageBoxButtons.OK, icon);
    }

    private static bool Expected(Exception error) => Faults.Expected(error) ||
        error is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException;

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        stop.Cancel();
        stop.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr position, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
