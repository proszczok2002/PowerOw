using Microsoft.Win32;

namespace PowerOw;

internal static class ExtendedEntries
{
    private sealed record Location(string Category, string Path, bool PerUser = false,
        bool BothViews = false, bool Children = false, string[]? Names = null);

    private static readonly Location[] Locations =
    [
        new("Boot", @"SYSTEM\CurrentControlSet\Control\Session Manager", Names: ["BootExecute", "SetupExecute", "Execute"]),
        new("KnownDlls", @"SYSTEM\CurrentControlSet\Control\Session Manager\KnownDLLs"),
        new("AppCert", @"SYSTEM\CurrentControlSet\Control\Session Manager\AppCertDlls"),
        new("Winlogon", RegistryEntries.WindowsNt + "Winlogon", PerUser: true, Names: ["Shell", "Userinit", "Taskman", "VmApplet", "System"]),
        new("Winlogon", RegistryEntries.WindowsNt + @"Winlogon\Notify", Children: true, Names: ["DLLName"]),
        new("WindowsLoad", RegistryEntries.WindowsNt + "Windows", PerUser: true, BothViews: true, Names: ["load", "run", "AppInit_DLLs"]),
        new("ActiveSetup", @"SOFTWARE\Microsoft\Active Setup\Installed Components", BothViews: true, Children: true, Names: ["StubPath"]),
        new("RunOnceEx", RegistryEntries.Current + "RunOnceEx", BothViews: true, Children: true),
        new("ImageOptions", RegistryEntries.WindowsNt + "Image File Execution Options", Children: true, Names: ["Debugger", "VerifierDlls"]),
        new("ImageOptions", RegistryEntries.WindowsNt + "SilentProcessExit", Children: true, Names: ["MonitorProcess"]),
        new("ShellExtensions", RegistryEntries.Current + @"Shell Extensions\Approved", PerUser: true, BothViews: true),
        new("ShellExtensions", RegistryEntries.Current + @"Explorer\ShellExecuteHooks", PerUser: true, BothViews: true),
        new("ShellExtensions", RegistryEntries.Current + @"Explorer\ShellIconOverlayIdentifiers", PerUser: true, BothViews: true, Children: true, Names: [""]),
        new("ShellExtensions", RegistryEntries.Current + @"Explorer\SharedTaskScheduler", PerUser: true, BothViews: true),
        new("ShellExtensions", RegistryEntries.Current + @"Explorer\Desktop\NameSpace", PerUser: true, Children: true, Names: [""]),
        new("ShellExtensions", @"Software\Classes\*\shellex\ContextMenuHandlers", PerUser: true, Children: true, Names: [""]),
        new("ShellExtensions", @"Software\Classes\Directory\shellex\ContextMenuHandlers", PerUser: true, Children: true, Names: [""]),
        new("ShellExtensions", @"Software\Classes\Folder\shellex\ContextMenuHandlers", PerUser: true, Children: true, Names: [""]),
        new("ShellExtensions", @"Software\Classes\Drive\shellex\ContextMenuHandlers", PerUser: true, Children: true, Names: [""]),
        new("BrowserExtensions", RegistryEntries.Current + @"Explorer\Browser Helper Objects", PerUser: true, BothViews: true, Children: true, Names: [""]),
        new("BrowserExtensions", @"Software\Microsoft\Internet Explorer\Toolbar", PerUser: true, BothViews: true),
        new("BrowserExtensions", @"Software\Microsoft\Internet Explorer\Extensions", PerUser: true, BothViews: true, Children: true, Names: ["Exec", "Script", "ClsidExtension"]),
        new("Codecs", RegistryEntries.WindowsNt + "Drivers32", BothViews: true),
        new("Codecs", @"Software\Classes\MediaFoundation\Transforms", BothViews: true, Children: true, Names: [""]),
        new("Lsa", @"SYSTEM\CurrentControlSet\Control\Lsa", Names: ["Authentication Packages", "Notification Packages", "Security Packages"]),
        new("Print", @"SYSTEM\CurrentControlSet\Control\Print\Monitors", Children: true, Names: ["Driver"]),
        new("Network", @"SYSTEM\CurrentControlSet\Control\NetworkProvider\Order", Names: ["ProviderOrder"]),
        new("Network", @"SYSTEM\CurrentControlSet\Services\WinSock2\Parameters\NameSpace_Catalog5\Catalog_Entries", Children: true, Names: ["LibraryPath"]),
        new("Network", @"SYSTEM\CurrentControlSet\Services\WinSock2\Parameters\NameSpace_Catalog5\Catalog_Entries64", Children: true, Names: ["LibraryPath"]),
    ];

    internal static void Registry(Scan scan, bool includeUser, bool profiles, bool onlyUser = false)
    {
        foreach (var location in Locations)
        {
            if (!onlyUser)
            {
                ScanLocation(RegistryHive.LocalMachine, RegistryView.Registry64, StartupScope.Machine, location);
                if (location.BothViews)
                    ScanLocation(RegistryHive.LocalMachine, RegistryView.Registry32, StartupScope.Machine, location);
            }
            if (includeUser && location.PerUser)
                ScanLocation(RegistryHive.CurrentUser, RegistryView.Registry64, StartupScope.User, location);
        }
        if (profiles)
            OtherProfiles(scan);

        void ScanLocation(RegistryHive hive, RegistryView view, StartupScope scope, Location location)
        {
            try
            {
                using var key = RegistryEntries.Open(hive, view, location.Path);
                if (key is null)
                    return;
                string display = $"{(hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU")}\\{location.Path}";
                if (view == RegistryView.Registry32 && location.Path.StartsWith(@"Software\Classes\", StringComparison.OrdinalIgnoreCase))
                    return; // These class locations are shared.
                display += view == RegistryView.Registry32 ? " [32-bit]" : "";
                if (!location.Children)
                    ReadValues(key, display, "", location.Names);
                else
                {
                    foreach (string childName in key.GetSubKeyNames())
                    {
                        using var child = key.OpenSubKey(childName);
                        if (child is not null)
                            ReadValues(child, display + "\\" + childName, childName, location.Names);
                    }
                }
            }
            catch (Exception error) when (Faults.Expected(error)) { scan.Unavailable.Add(StartupKind.System); }

            void ReadValues(RegistryKey key, string display, string child, string[]? names)
            {
                foreach (string valueName in names ?? key.GetValueNames())
                {
                    object? value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    string? text = value switch { string line => line, string[] lines => string.Join(Environment.NewLine, lines), _ => null };
                    if (string.IsNullOrWhiteSpace(text) && !(location.Children && valueName.Length == 0))
                        continue;
                    string title = child.Length > 0 ? child : valueName;
                    if (title.Length == 0)
                        title = location.Category;
                    string command = text ?? title;
                    string? target = ResolveTarget(view, scope, valueName, title, command);
                    string fingerprint = display + "|" + valueName;
                    var id = new EntryId(StartupKind.System, scope, location.Category, fingerprint);
                    scan.Entries.Add(new StartupEntry(id, title, display, command, target,
                        EntryState.ReadOnly, EntryActions.None, fingerprint, valueName));
                }
            }
        }
    }

    private static string? ResolveTarget(RegistryView view, StartupScope scope, string name, string title, string command)
    {
        if (!Guid.TryParse(name, out var clsid) && !Guid.TryParse(title, out clsid) && !Guid.TryParse(command, out clsid))
            return CommandPath.Target(command);
        string path = $@"Software\Classes\CLSID\{clsid:B}\InprocServer32";
        using var user = scope == StartupScope.User ? RegistryEntries.Open(RegistryHive.CurrentUser, view, path) : null;
        using var machine = RegistryEntries.Open(RegistryHive.LocalMachine, view, path);
        return (user?.GetValue("") ?? machine?.GetValue("")) is string library ? CommandPath.Target(library) : null;
    }

    private static void OtherProfiles(Scan scan)
    {
        try
        {
            using var current = System.Security.Principal.WindowsIdentity.GetCurrent();
            using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
            foreach (string sid in users.GetSubKeyNames())
            {
                if ((!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) && !sid.StartsWith("S-1-12-1-", StringComparison.Ordinal))
                    || sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase) || sid == (AccountAccess.Sid ?? current.User?.Value))
                    continue;
                foreach (var site in RegistryEntries.Sites.Where(site => site.Scope == StartupScope.User))
                {
                    string path = $@"{sid}\{site.Path}";
                    using var key = users.OpenSubKey(path);
                    if (key is null)
                        continue;
                    foreach (string name in key.GetValueNames())
                    {
                        if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string text)
                            continue;
                        var id = new EntryId(StartupKind.System, StartupScope.Machine, "OtherProfiles", path + "|" + name);
                        scan.Entries.Add(new StartupEntry(id, name, "HKU\\" + path, text, null,
                            EntryState.ReadOnly, EntryActions.None, id.Name, sid));
                    }
                }
            }
        }
        catch (Exception error) when (Faults.Expected(error)) { scan.Unavailable.Add(StartupKind.System); }
    }

    internal static void Wmi(Scan scan)
    {
        try
        {
            using var locator = ComObject.Create("WbemScripting.SWbemLocator");
            using var services = new ComObject(locator.Api.ConnectServer(".", @"root\subscription"));
            using var bindings = new ComObject(services.Api.ExecQuery("SELECT * FROM __FilterToConsumerBinding", "WQL", 48));
            foreach (object instance in bindings.Api)
            {
                using var binding = new ComObject(instance);
                string consumer = binding.Api.Consumer;
                string filter = binding.Api.Filter;
                string location = @"root\subscription\__FilterToConsumerBinding";
                var id = new EntryId(StartupKind.Wmi, StartupScope.Machine, location, consumer + "|" + filter);
                scan.Entries.Add(new StartupEntry(id, consumer, location, filter, null,
                    EntryState.ReadOnly, EntryActions.None, id.Name));
            }
        }
        catch (Exception error) when (Faults.Expected(error)) { scan.Unavailable.Add(StartupKind.Wmi); }
    }
}
