using Microsoft.Win32;

namespace PowerOw;

internal static class RegistryEntries
{
    internal const string Current = @"Software\Microsoft\Windows\CurrentVersion\";
    internal const string WindowsNt = @"Software\Microsoft\Windows NT\CurrentVersion\";
    private const string Approval = Current + @"Explorer\StartupApproved\";

    internal sealed record Site(string Code, StartupScope Scope, RegistryView View, string Path, string? ApprovalName, bool Editable)
    {
        internal RegistryHive Hive => Scope == StartupScope.User ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
        internal string Display => $"{(Scope == StartupScope.User ? "HKCU" : "HKLM")}\\{Path}"
            + (View == RegistryView.Registry32 ? " [32-bit]" : "");
        internal RegistryKey? Open(bool write = false) => RegistryEntries.Open(Hive, View, Path, write);
    }

    internal static readonly Site[] Sites =
    [
        new("UserRun", StartupScope.User, RegistryView.Registry64, Current + "Run", "Run", true),
        new("UserRunOnce", StartupScope.User, RegistryView.Registry64, Current + "RunOnce", null, true),
        new("UserPolicy", StartupScope.User, RegistryView.Registry64, Current + @"Policies\Explorer\Run", null, false),
        new("MachineRun", StartupScope.Machine, RegistryView.Registry64, Current + "Run", "Run", true),
        new("MachineRun32", StartupScope.Machine, RegistryView.Registry32, Current + "Run", "Run32", true),
        new("MachineRunOnce", StartupScope.Machine, RegistryView.Registry64, Current + "RunOnce", null, true),
        new("MachineRunOnce32", StartupScope.Machine, RegistryView.Registry32, Current + "RunOnce", null, true),
        new("MachinePolicy", StartupScope.Machine, RegistryView.Registry64, Current + @"Policies\Explorer\Run", null, false),
        new("MachinePolicy32", StartupScope.Machine, RegistryView.Registry32, Current + @"Policies\Explorer\Run", null, false),
    ];

    internal static RegistryKey? Open(RegistryHive hive, RegistryView view, string path, bool write = false)
    {
        using var root = hive == RegistryHive.CurrentUser ? AccountAccess.OpenUser(view) : RegistryKey.OpenBaseKey(hive, view);
        return root.OpenSubKey(path, write);
    }

    internal static void Enumerate(Scan scan, bool includeUser, bool onlyUser = false)
    {
        foreach (var site in Sites.Where(site => onlyUser ? site.Scope == StartupScope.User : includeUser || site.Scope == StartupScope.Machine))
        {
            try
            {
                using var key = site.Open();
                if (key is null)
                    continue;
                foreach (string name in key.GetValueNames())
                {
                    var item = Read(site, key, name, includeUser);
                    if (item is not null)
                        scan.Entries.Add(item);
                }
            }
            catch (Exception error) when (Faults.Expected(error)) { scan.Unavailable.Add(StartupKind.Registry); }
        }
    }

    internal static StartupEntry? Read(EntryId id, bool userApproval)
    {
        Site site = GetSite(id);
        using var key = site.Open();
        return key is null ? null : Read(site, key, id.Name, userApproval);
    }

    private static StartupEntry? Read(Site site, RegistryKey key, string name, bool userApproval)
    {
        if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string text)
            return null;
        var type = key.GetValueKind(name);
        bool own = string.Equals(name, "PowerOw", StringComparison.OrdinalIgnoreCase)
            && site.Code == "MachineRun";
        bool disabled = site.ApprovalName != null && IsWindowsDisabled(site.Scope, site.ApprovalName, name, userApproval);
        EntryActions actions = site.Editable && !own ? EntryActions.Disable | EntryActions.Remove : EntryActions.None;
        if (site.Code == "UserRun" && !disabled && CommandPath.CanDelay(text, type).HasValue)
            actions |= EntryActions.Delay;
        var id = new EntryId(StartupKind.Registry, site.Scope, site.Code, name);
        string? target = CommandPath.Target(text);
        bool system = own || (name.Equals("SecurityHealth", StringComparison.OrdinalIgnoreCase)
            && string.Equals(target, Path.Combine(Environment.SystemDirectory, "SecurityHealthSystray.exe"), StringComparison.OrdinalIgnoreCase));
        return new StartupEntry(id, name, site.Display, text, target,
            disabled ? EntryState.WindowsDisabled : EntryState.Active, actions, $"{type}\n{text}", WindowsComponent: system);
    }

    internal static bool IsWindowsDisabled(StartupScope scope, string location, string name, bool userOverride)
    {
        // Explorer's observed 12-byte records are display hints. Writes use the original entry and preserve Explorer data.
        if (userOverride && ReadApproval(RegistryHive.CurrentUser) is bool user)
            return user;
        return scope == StartupScope.Machine && ReadApproval(RegistryHive.LocalMachine) is true;

        bool? ReadApproval(RegistryHive hive)
        {
            using var key = Open(hive, RegistryView.Registry64, Approval + location);
            return key?.GetValue(name) switch
            {
                byte[] { Length: 12 } blob when blob[1] == 0 && blob[2] == 0 && blob[3] == 0 => blob[0] switch
                {
                    2 or 6 => false,
                    3 or 7 => true,
                    _ => null
                },
                _ => null
            };
        }
    }

    internal static Snapshot Capture(StartupEntry entry)
    {
        Site site = WritableSite(entry.Id);
        using var key = site.Open() ?? throw new StartupException(Problem.Changed);
        var type = key.GetValueKind(entry.Id.Name);
        if (type is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
            throw new StartupException(Problem.Changed);
        if ($"{type}\n{key.GetValue(entry.Id.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)}" != entry.Revision)
            throw new StartupException(Problem.Changed);
        return new Snapshot(entry, RegistryType: type.ToString());
    }

    internal static void Remove(StartupEntry entry)
    {
        Site site = WritableSite(entry.Id);
        using var key = site.Open(write: true) ?? throw new StartupException(Problem.Changed);
        if (Read(site, key, entry.Id.Name, userApproval: false)?.Revision != entry.Revision)
            throw new StartupException(Problem.Changed);
        key.DeleteValue(entry.Id.Name, throwOnMissingValue: true);
    }

    internal static void Restore(Snapshot snapshot)
    {
        Site site = WritableSite(snapshot.Entry.Id);
        using var root = site.Scope == StartupScope.User ? AccountAccess.OpenUser(site.View) : RegistryKey.OpenBaseKey(site.Hive, site.View);
        using var key = root.CreateSubKey(site.Path, writable: true);
        if (key.GetValue(snapshot.Entry.Id.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not null)
            throw new StartupException(Problem.Occupied);
        var type = snapshot.RegistryType switch
        {
            nameof(RegistryValueKind.String) => RegistryValueKind.String,
            nameof(RegistryValueKind.ExpandString) => RegistryValueKind.ExpandString,
            _ => throw new StartupException(Problem.InvalidStore)
        };
        key.SetValue(snapshot.Entry.Id.Name, snapshot.Entry.Command, type);
    }

    private static Site GetSite(EntryId id) => Sites.FirstOrDefault(site => site.Scope == id.Scope && site.Code == id.Site)
        ?? throw new StartupException(Problem.BadTarget);

    private static Site WritableSite(EntryId id)
    {
        var site = GetSite(id);
        if (!site.Editable || id.Name.Length == 0 ||
            (site.Code == "MachineRun" && string.Equals(id.Name, "PowerOw", StringComparison.OrdinalIgnoreCase)))
            throw new StartupException(Problem.ReadOnly);
        return site;
    }
}

internal static class CommandPath
{
    internal static string? Target(string command)
    {
        string value = Environment.ExpandEnvironmentVariables(command.Trim());
        if (value.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            value = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + value[11..];
        else if (value.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
            value = value[4..];
        else if (value.StartsWith(@"system32\", StringComparison.OrdinalIgnoreCase))
            value = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), value);
        var (target, _) = Split(value, allowUnquotedSpaces: true);
        return IsLocalPath(target) ? target : null;
    }

    internal static (string File, string Arguments)? CanDelay(string command, RegistryValueKind kind)
    {
        if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
            return null;
        var (file, args) = Split(command.Trim(), allowUnquotedSpaces: false);
        file = Environment.ExpandEnvironmentVariables(file);
        if (!IsLocalPath(file) || !file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || args.Contains('\0'))
            return null;
        if (kind == RegistryValueKind.ExpandString)
            args = Environment.ExpandEnvironmentVariables(args);
        return (file, args);
    }

    internal static bool IsLocalPath(string? path) =>
        path is { Length: >= 3 } && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\'
        && !path.AsSpan(2).Contains(':') && path.IndexOfAny(Path.GetInvalidPathChars()) < 0;

    private static (string, string) Split(string value, bool allowUnquotedSpaces)
    {
        if (value.StartsWith('"'))
        {
            int end = value.IndexOf('"', 1);
            return end > 0 && (end + 1 == value.Length || char.IsWhiteSpace(value[end + 1]))
                ? (value[1..end], value[(end + 1)..].TrimStart()) : ("", "");
        }
        if (allowUnquotedSpaces)
        {
            foreach (string extension in new[] { ".exe", ".dll", ".sys", ".cmd", ".bat", ".vbs", ".ps1", ".scr", ".com" })
            {
                int end = value.IndexOf(extension, StringComparison.OrdinalIgnoreCase) + extension.Length;
                if (end >= extension.Length && (end == value.Length || value[end] is ' ' or '\t' or ','))
                    return (value[..end], value[end..].TrimStart());
            }
        }
        int space = value.IndexOfAny([' ', '\t']);
        return space < 0 ? (value, "") : (value[..space], value[space..].TrimStart());
    }
}
