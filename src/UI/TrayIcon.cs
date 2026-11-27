using System.Runtime.InteropServices;

namespace PowerOw;

internal sealed class TrayIcon : NativeWindow, IDisposable
{
    private const int Callback = 0x8000 + 18;
    private const int Select = 0x0400, KeySelect = 0x0401, ContextMenu = 0x007B;
    private const uint MessageAndIcon = 1 | 2 | 4 | 0x20 | 0x80;
    private static readonly Guid Id = new("F33FD84F-EC00-4A4D-BF1D-B56890B2E48A");
    private readonly Icon icon;
    private readonly AnnouncedMenu commands = new();
    private readonly int taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private string title = "PowerOw";
    private bool added;

    internal Action? Open { get; set; }
    internal Func<IReadOnlyList<(string Label, Action Run)>>? Menu { get; set; }

    internal TrayIcon()
    {
        icon = Appearance.Icon();
        CreateHandle(new CreateParams { Caption = "PowerOw notification area" });
        _ = ChangeWindowMessageFilterEx(Handle, Callback, 1, IntPtr.Zero);
        _ = ChangeWindowMessageFilterEx(Handle, taskbarCreated, 1, IntPtr.Zero);
        commands.Closed += (_, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.Keyboard && added)
            {
                var data = Data(0x20);
                _ = ShellNotify(3, ref data);
            }
        };
    }

    internal void SetText(string value, string menu)
    {
        title = Trim(value, 127);
        commands.AccessibleName = menu;
        if (added)
        {
            var data = Data(MessageAndIcon);
            _ = ShellNotify(1, ref data);
        }
        else
            Add();
    }

    internal void Notify(string heading, string message, bool sound = false)
    {
        if (!added)
            Add();
        if (!added)
            return;
        var data = Data(0x10 | 0x20);
        data.InfoTitle = Trim(heading, 63);
        data.Info = Trim(message, 255);
        data.InfoFlags = sound ? 1u : 1u | 0x10; // NIIF_INFO, optionally NIIF_NOSOUND
        _ = ShellNotify(1, ref data);
    }

    private void Add()
    {
        var data = Data(MessageAndIcon);
        added = ShellNotify(0, ref data);
        if (added)
        {
            data.Version = 4;
            _ = ShellNotify(4, ref data);
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == taskbarCreated)
            Add();
        if (message.Msg == Callback)
        {
            switch (message.LParam.ToInt64() & 0xFFFF)
            {
                case Select:
                case KeySelect:
                case 0x0405: // NIN_BALLOONUSERCLICK
                    Open?.Invoke();
                    return;
                case ContextMenu:
                case 0x0205: // WM_RBUTTONUP if version negotiation failed.
                    ShowMenu();
                    return;
            }
        }
        base.WndProc(ref message);
    }

    private void ShowMenu()
    {
        var options = Menu?.Invoke();
        if (options is not { Count: > 0 })
            return;
        foreach (var item in commands.Items.Cast<ToolStripItem>().ToArray())
            item.Dispose();
        foreach (var option in options)
        {
            var item = new ToolStripMenuItem(option.Label) { AccessibleName = TextCatalog.Plain(option.Label) };
            item.Click += (_, _) => option.Run();
            commands.Items.Add(item);
        }
        var point = Cursor.Position;
        var lookup = new IconId { Size = (uint)Marshal.SizeOf<IconId>(), Window = Handle, Id = 1, Guid = Id };
        if (Shell_NotifyIconGetRect(ref lookup, out var rectangle) == 0)
            point = new Point(rectangle.Left, rectangle.Top);
        _ = SetForegroundWindow(Handle);
        commands.Show(point, ToolStripDropDownDirection.AboveRight);
        // The notification window can retain keyboard focus when the main window is hidden.
        commands.Focus();
        _ = PostMessage(Handle, 0, IntPtr.Zero, IntPtr.Zero);
    }

    internal static string Trim(string value, int max)
    {
        if (value.Length <= max)
            return value;
        return value[..(char.IsHighSurrogate(value[max - 1]) ? max - 1 : max)];
    }

    private IconData Data(uint flags) => new()
    {
        Size = (uint)Marshal.SizeOf<IconData>(), Window = Handle, Id = 1, Flags = flags,
        Callback = Callback, Icon = icon.Handle, Tip = title, Info = "", InfoTitle = "", Guid = Id
    };

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            if (added)
            {
                var data = Data(0x20);
                _ = ShellNotify(2, ref data);
            }
            DestroyHandle();
        }
        commands.Dispose();
        icon.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr Balloon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconId
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public Guid Guid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    private static extern bool ShellNotify(uint operation, ref IconData data);
    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref IconId id, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string name);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr window, int message, uint action, IntPtr info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}

