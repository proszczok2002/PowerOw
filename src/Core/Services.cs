using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace PowerOw;

internal static class Services
{
    internal const string RegistryPath = @"SYSTEM\CurrentControlSet\Services";
    internal const int Auto = 2, Demand = 3, Disabled = 4;

    internal static void Enumerate(Scan scan, bool applicationsOnly = false, bool startupOnly = false)
    {
        try
        {
            using var root = RegistryEntries.Open(RegistryHive.LocalMachine, RegistryView.Registry64, RegistryPath);
            if (root is null)
                return;
            foreach (string name in root.GetSubKeyNames())
            {
                try
                {
                    using var key = root.OpenSubKey(name);
                    if (key is not null && (!startupOnly || key.GetValue("Start") is Auto) &&
                        Describe(name, key, includeManual: false) is { } item &&
                        (!applicationsOnly || item is { Id.Kind: StartupKind.Service, WindowsComponent: false }))
                        scan.Entries.Add(item);
                }
                catch (Exception error) when (Faults.Expected(error))
                {
                    if (!applicationsOnly)
                        scan.Unavailable.Add(StartupKind.Service);
                }
            }
        }
        catch (Exception error) when (Faults.Expected(error)) { scan.Unavailable.Add(StartupKind.Service); }
    }

    internal static StartupEntry? Read(EntryId id)
    {
        Validate(id);
        using var key = RegistryEntries.Open(RegistryHive.LocalMachine, RegistryView.Registry64, $@"{RegistryPath}\{id.Name}");
        return key is null ? null : Describe(id.Name, key, includeManual: true);
    }

    private static StartupEntry? Describe(string name, RegistryKey key, bool includeManual)
    {
        if (key.GetValue("Type") is not int type || key.GetValue("Start") is not int start)
            return null;
        bool driver = (type & 0x0F) != 0;
        bool win32 = (type & 0x30) != 0;
        using var triggerKey = key.OpenSubKey("TriggerInfo");
        bool trigger = win32 && triggerKey?.SubKeyCount > 0;
        if ((!driver && !win32) || (!includeManual && (driver ? start >= Disabled : start == Demand && !trigger)))
            return null;
        string command = key.GetValue("ImagePath", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        string? target = CommandPath.Target(command);
        using var parameters = key.OpenSubKey("Parameters");
        string? library = parameters?.GetValue("ServiceDll") as string;
        var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\";
        bool system = name.Equals(InstallPaths.ServiceName, StringComparison.OrdinalIgnoreCase)
            || target is null || target.StartsWith(systemDir, StringComparison.OrdinalIgnoreCase)
            || (CommandPath.IsLocalPath(library) && library!.StartsWith(systemDir, StringComparison.OrdinalIgnoreCase));
        var id = new EntryId(driver ? StartupKind.Driver : StartupKind.Service, StartupScope.Machine, "", name);
        bool delayed = key.GetValue("DelayedAutoStart") is 1;
        string title = key.GetValue("DisplayName") as string ?? name;
        if (title.StartsWith('@'))
            title = name;
        return new StartupEntry(id, title, $@"HKLM\{RegistryPath}\{name}", command, library ?? target,
            start == Disabled ? EntryState.WindowsDisabled : start == Demand ? EntryState.Manual : EntryState.Active,
            driver || system || (type & 0x40) != 0 ? EntryActions.None : EntryActions.Disable,
            FormattableString.Invariant($"{type}|{start}|{delayed}|{command}|{library}|{key.GetValue("ObjectName")}"),
            name, system);
    }

    internal static Snapshot Capture(StartupEntry entry)
    {
        using var service = OpenEditable(entry.Id);
        var config = Query(service);
        int startType = (int)config.StartType;
        bool delayed = QueryDelayed(service);
        if (Read(entry.Id)?.Revision != entry.Revision || startType is not (Auto or Demand))
            throw new StartupException(Problem.Changed);
        return new Snapshot(entry, StartType: startType, DelayedAuto: delayed, Definition: Definition(entry));
    }

    internal static void Disable(StartupEntry entry)
    {
        using var service = OpenEditable(entry.Id);
        if (Read(entry.Id)?.Revision != entry.Revision)
            throw new StartupException(Problem.Changed);
        SetStart(service, Disabled);
    }

    internal static bool CanRestore(Snapshot snapshot, StartupEntry current) =>
        current.Actions.HasFlag(EntryActions.Disable) && snapshot.Definition == Definition(current);

    internal static void Restore(Snapshot snapshot, StartupEntry current)
    {
        using var service = OpenEditable(current.Id);
        if (!CanRestore(snapshot, current) || Read(current.Id)?.Revision != current.Revision)
            throw new StartupException(Problem.Changed);
        if (snapshot.StartType is not (Auto or Demand))
            throw new StartupException(Problem.InvalidStore);
        SetStart(service, (uint)snapshot.StartType);
        if (snapshot.StartType == Auto && QueryDelayed(service) != snapshot.DelayedAuto)
        {
            int delayed = snapshot.DelayedAuto ? 1 : 0;
            if (!ChangeServiceConfig2(service, 3, ref delayed))
            {
                int error = Marshal.GetLastWin32Error();
                SetStart(service, Disabled);
                throw new Win32Exception(error);
            }
        }
    }

    private static string Definition(StartupEntry entry)
    {
        string[] parts = entry.Revision.Split('|', 4);
        return parts[0] + "|" + parts[3];
    }

    private static ServiceHandle OpenEditable(EntryId id)
    {
        Validate(id);
        if (Read(id) is not { Actions: EntryActions.Disable } || id.Kind != StartupKind.Service)
            throw new StartupException(Problem.ReadOnly);
        using var manager = OpenSCManager(null, null, 1);
        if (manager.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var service = OpenService(manager, id.Name, 3);
        if (service.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            service.Dispose();
            throw new Win32Exception(error);
        }
        return service;
    }

    private static void Validate(EntryId id)
    {
        if (id.Scope != StartupScope.Machine || id.Site.Length != 0 || string.IsNullOrWhiteSpace(id.Name)
            || id.Name.IndexOfAny(['\\', '/', '\0']) >= 0)
            throw new StartupException(Problem.BadTarget);
    }

    private static ServiceConfig Query(ServiceHandle handle)
    {
        _ = QueryServiceConfig(handle, IntPtr.Zero, 0, out uint bytes);
        if (bytes < Marshal.SizeOf<ServiceConfig>() || bytes > 65536)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr buffer = Marshal.AllocHGlobal((int)bytes);
        try
        {
            if (!QueryServiceConfig(handle, buffer, bytes, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return Marshal.PtrToStructure<ServiceConfig>(buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool QueryDelayed(ServiceHandle handle)
    {
        if (!QueryServiceConfig2(handle, 3, out int delayed, sizeof(int), out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return delayed != 0;
    }

    private static void SetStart(ServiceHandle handle, uint type)
    {
        if (!ChangeServiceConfig(handle, uint.MaxValue, type, uint.MaxValue, null, null, IntPtr.Zero, null, null, null, null))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    internal sealed class ServiceHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceConfig
    {
        public uint ServiceType, StartType, ErrorControl;
        public IntPtr Image, Group;
        public uint Tag;
        public IntPtr Dependencies, Account, Name;
    }

    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ServiceHandle OpenService(ServiceHandle manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr service);
    [DllImport("advapi32.dll", EntryPoint = "QueryServiceConfigW", SetLastError = true)]
    private static extern bool QueryServiceConfig(ServiceHandle handle, IntPtr buffer, uint length, out uint needed);
    [DllImport("advapi32.dll", EntryPoint = "QueryServiceConfig2W", SetLastError = true)]
    private static extern bool QueryServiceConfig2(ServiceHandle handle, uint level, out int buffer, uint length, out uint needed);
    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfigW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ChangeServiceConfig(ServiceHandle handle, uint serviceType, uint startType, uint errorControl,
        string? binaryPath, string? loadOrder, IntPtr tag, string? dependencies, string? account, string? password, string? display);
    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", SetLastError = true)]
    private static extern bool ChangeServiceConfig2(ServiceHandle handle, uint level, ref int information);
}
