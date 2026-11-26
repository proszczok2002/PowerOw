using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PowerOw;

internal sealed class AnnouncedMenu : ContextMenuStrip
{
    protected override void OnOpening(CancelEventArgs e)
    {
        _ = AccessibilityObject;
        foreach (ToolStripItem item in Items)
            _ = item.AccessibilityObject;
        base.OnOpening(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (!IsHandleCreated)
            return;
        // WinForms does not initially select an item in a ContextMenuStrip.
        // Prime the providers before opening and move accessibility focus to the first usable command.
        NotifyWinEvent(0x0006, Handle, -4, 0); // EVENT_SYSTEM_MENUPOPUPSTART
        BeginInvoke(() =>
        {
            if (!Visible || Items.OfType<ToolStripMenuItem>().FirstOrDefault(item => item.Available && item.Enabled) is not { } first)
                return;
            first.Select();
            NotifyWinEvent(0x8005, Handle, -4, Items.IndexOf(first) + 1); // EVENT_OBJECT_FOCUS
        });
    }

    [DllImport("user32.dll")]
    private static extern void NotifyWinEvent(uint eventId, IntPtr window, int objectId, int childId);
}

internal sealed class SpokenCheckBox : CheckBox
{
    private static readonly MethodInfo? Raise = typeof(AccessibleObject).GetMethod("RaiseAutomationPropertyChangedEvent",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly ParameterInfo[]? Arguments = Raise?.GetParameters();
    private static readonly MethodInfo? ConvertValue = Arguments is { Length: 3 }
        ? Arguments[1].ParameterType.GetMethod("op_Explicit", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, [typeof(int)])
        : null;

    protected override void OnCheckedChanged(EventArgs e)
    {
        bool value = Checked;
        base.OnCheckedChanged(e);
        if (!IsHandleCreated || Checked != value)
            return;
        NotifyWinEvent(0x800A, Handle, -4, 0); // EVENT_OBJECT_STATECHANGE
        if (Arguments is not { Length: 3 } || ConvertValue is null || !UiaClientsAreListening())
            return;
        var property = Enum.ToObject(Arguments[0].ParameterType, 30086);
        var oldValue = ConvertValue.Invoke(null, [value ? 0 : 1]);
        var newValue = ConvertValue.Invoke(null, [value ? 1 : 0]);
        try { Raise!.Invoke(AccessibilityObject, [property, oldValue, newValue]); }
        finally
        {
            (oldValue as IDisposable)?.Dispose();
            (newValue as IDisposable)?.Dispose();
        }
    }

    [DllImport("uiautomationcore.dll", ExactSpelling = true)]
    private static extern bool UiaClientsAreListening();
    [DllImport("user32.dll")]
    private static extern void NotifyWinEvent(uint eventId, IntPtr window, int objectId, int childId);
}
