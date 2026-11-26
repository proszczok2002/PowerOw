using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PowerOw;

internal enum ThemeMode { System, Light, Dark }

internal static class Appearance
{
    internal static Color Navy => Color.FromArgb(26, 39, 56);
    private static Color DarkInput => Color.FromArgb(12, 20, 31);
    private static Color DarkText => Color.FromArgb(244, 248, 252);
    private static Color DarkBorder => Color.FromArgb(126, 146, 169);
    private static Color DarkSelected => Color.FromArgb(50, 75, 102);
    private static Color LightSurface => Color.FromArgb(238, 243, 249);
    private static Color LightText => Color.FromArgb(22, 40, 62);
    private static readonly ToolStripRenderer DarkMenu = new ToolStripProfessionalRenderer(new MenuColors());
    private static readonly ToolStripRenderer SystemMenu = new ToolStripSystemRenderer();

    internal static Icon Icon()
    {
        using var stream = typeof(Appearance).Assembly.GetManifestResourceStream("PowerOw.Icon")!;
        using var icon = new Icon(stream, new Size(64, 64));
        return (Icon)icon.Clone();
    }

    internal static bool Dark(ThemeMode theme) => !SystemInformation.HighContrast &&
        (theme == ThemeMode.Dark || (theme == ThemeMode.System && WindowsDark()));

    private static bool WindowsDark()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is 0;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (System.Security.SecurityException) { return false; }
    }

    internal static void SetNative(ThemeMode theme)
    {
#if !NETFRAMEWORK
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            Application.SetColorMode(Dark(theme) ? SystemColorMode.Dark : SystemColorMode.Classic);
#endif
    }

    internal static void Apply(Form form, ThemeMode theme)
    {
        bool dark = Dark(theme);
        bool contrast = SystemInformation.HighContrast;
        Colorize(form, dark, contrast);
        if (form.IsHandleCreated)
        {
            int enabled = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
        }
    }

    private static void Colorize(Control control, bool dark, bool contrast)
    {
        bool input = control is TextBoxBase or ListBox or ComboBox or NumericUpDown;
        control.BackColor = dark ? (input ? DarkInput : Navy) : input ? SystemColors.Window : contrast ? SystemColors.Control : LightSurface;
        control.ForeColor = dark ? DarkText : contrast ? SystemColors.ControlText : LightText;
        if (control is Button button)
        {
            button.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
            button.UseVisualStyleBackColor = !dark;
            button.FlatAppearance.BorderColor = DarkBorder;
            button.FlatAppearance.MouseOverBackColor = DarkSelected;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(22, 46, 69);
        }
        if (control is ToolStrip menu)
            StyleMenu(menu, dark);
        if (control.ContextMenuStrip is ToolStrip context)
            StyleMenu(context, dark);
        if (control.IsHandleCreated && control is ComboBox or ListBox or TextBoxBase)
            _ = SetWindowTheme(control.Handle, dark ? (control is ComboBox ? "DarkMode_CFD" : "DarkMode_Explorer") : null, null);
        foreach (Control child in control.Controls)
            Colorize(child, dark, contrast);
        control.Invalidate();
    }

    private static void StyleMenu(ToolStrip menu, bool dark)
    {
        menu.Renderer = dark ? DarkMenu : SystemMenu;
        foreach (ToolStripItem item in menu.Items)
            ColorMenu(item, dark);
    }

    private static void ColorMenu(ToolStripItem item, bool dark)
    {
        item.BackColor = dark ? Navy : SystemColors.Menu;
        item.ForeColor = dark ? DarkText : SystemColors.MenuText;
        if (item is ToolStripDropDownItem dropDown)
            foreach (ToolStripItem entry in dropDown.DropDownItems)
                ColorMenu(entry, dark);
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => Navy;
        public override Color MenuStripGradientEnd => Navy;
        public override Color ToolStripDropDownBackground => Navy;
        public override Color MenuItemSelected => DarkSelected;
        public override Color MenuItemSelectedGradientBegin => DarkSelected;
        public override Color MenuItemSelectedGradientEnd => DarkSelected;
        public override Color MenuItemPressedGradientBegin => DarkSelected;
        public override Color MenuItemPressedGradientMiddle => DarkSelected;
        public override Color MenuItemPressedGradientEnd => DarkSelected;
        public override Color MenuItemBorder => DarkBorder;
        public override Color MenuBorder => DarkBorder;
        public override Color ImageMarginGradientBegin => Navy;
        public override Color ImageMarginGradientMiddle => Navy;
        public override Color ImageMarginGradientEnd => Navy;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int bytes);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetWindowTheme(IntPtr window, string? application, string? identifiers);
}

