using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] is not ("pl" or "en" or "de" or "ru" or "hr" or "fr" or "es" or "zh") ||
            args[1] is not ("0" or "1"))
            return 5;
        try
        {
            string staging = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            string name = Path.GetFileName(staging);
            const string prefix = "PowerOw-update-";
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var registration = machine.OpenSubKey(@"Software\Proszczok\PowerOw");
            if (!(registration?.GetValue("Directory") is string installed) ||
                !string.Equals(Path.GetDirectoryName(staging), installed.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
                !name.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name.Substring(prefix.Length), "N", out _))
                return 5;
            using var setup = Process.Start(new ProcessStartInfo(Path.Combine(staging, "PowerOw-Setup.exe"))
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = staging,
                Arguments = $"/SILENT /NORESTART /SP- /NOCANCEL /POWEROWUPDATE=1 /POWEROWTRAY={args[1]} /LANG={args[0]}"
            });
            if (setup == null)
                return 1;
            setup.WaitForExit();
            return setup.ExitCode;
        }
        catch (Win32Exception error) { return error.NativeErrorCode; }
        catch (IOException) { return 1; }
        catch (UnauthorizedAccessException) { return 5; }
    }
}
