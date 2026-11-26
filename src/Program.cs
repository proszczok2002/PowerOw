using System.Security.Principal;
using System.ServiceProcess;

namespace PowerOw;

internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        using var identity = WindowsIdentity.GetCurrent();
        bool admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        string mode = arguments.Length > 0 ? arguments[0] : "";
        if (mode == "--service")
        {
            if (!identity.IsSystem || Environment.UserInteractive || !InstallPaths.IsInstalled)
                return 5;
            ServiceBase.Run(new GuardianService());
            return 0;
        }
        if (mode is "--setup-install" or "--setup-stop" or "--setup-remove")
        {
            if (!admin || !InstallPaths.IsInstalled)
                return 5;
            try
            {
                bool? logon = ReadOption("--logon");
                bool? protection = ReadOption("--protection");
                Maintenance.Run(mode, logon, protection);
                return 0;
            }
            catch (Exception error) when (Faults.Expected(error) || error is InvalidOperationException or System.TimeoutException or System.ServiceProcess.TimeoutException)
            {
                try
                {
                    string data = StateStore.Location(StartupScope.Machine);
                    SafePaths.RequirePlain(data);
                    File.WriteAllText(Path.Combine(data, "maintenance-error.log"), $"{DateTimeOffset.Now:O} {mode}{Environment.NewLine}{error}");
                }
                catch (Exception logError) when (Faults.Expected(logError)) { }
                return error is System.ComponentModel.Win32Exception native ? native.NativeErrorCode : 1;
            }

            bool? ReadOption(string name) => arguments.Contains(name + "=1", StringComparer.Ordinal) ? true :
                arguments.Contains(name + "=0", StringComparer.Ordinal) ? false : null;
        }
        if (!admin || !InstallPaths.IsInstalled)
            return 5;
        _ = CoInitializeSecurity(IntPtr.Zero, -1, IntPtr.Zero, IntPtr.Zero, 0, 3, IntPtr.Zero, 0x40, IntPtr.Zero);
        AccountAccess.Initialize();
        var preferences = AccountAccess.Run(() => Preferences.Load(AccountAccess.Available));
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Appearance.SetNative(preferences.Theme);
        string prefix = $@"Local\Proszczok.PowerOw.{identity.User!.Value}";
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + ".Open");
        using var mutex = new Mutex(true, prefix, out bool first);
        bool startInTray = mode == "--tray";
        if (!first)
        {
            if (!startInTray)
                showEvent.Set();
            return 0;
        }
        try
        {
            var user = AccountAccess.Available ? new StartupEngine(StartupScope.User, new StateStore(StateStore.Location(StartupScope.User))) : null;
            var system = new StartupEngine(StartupScope.Machine, new StateStore(StateStore.Location(StartupScope.Machine)), user, applicationsOnly: true);
            using var host = new DesktopSession(system, preferences, startInTray, showEvent);
            Application.Run(host);
        }
        finally { mutex.ReleaseMutex(); AccountAccess.Dispose(); }
        return 0;
    }

    [System.Runtime.InteropServices.DllImport("ole32.dll")]
    private static extern int CoInitializeSecurity(IntPtr descriptor, int services, IntPtr auth, IntPtr reserved1,
        uint authenticationLevel, uint impersonationLevel, IntPtr identities, uint capabilities, IntPtr reserved3);
}
