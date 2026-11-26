namespace PowerOw;

internal enum StartupKind { Registry, StartupFolder, Task, Service, Driver, System, Wmi, Packaged }
internal enum StartupScope { User, Machine }

[Flags]
internal enum EntryActions { None = 0, Disable = 1, Remove = 2, Enable = 4, Delay = 8 }

internal enum EntryState { Active, WindowsDisabled, Manual, Disabled, Removed, Delayed, ReadOnly }
internal enum SavedMode { Disabled, Removed, Delayed }
internal enum Change { Disable, Remove, DisableAndGuard, RemoveAndGuard, Restore, Enable, StopGuarding, Delay, ForgetBackup }

internal enum ActivityKind { Disabled, Removed, Reenabled, RuleRemoved, Blocked, Failed, Delayed }
internal enum Problem
{
    Changed, Occupied, Unavailable, ReadOnly, AccessDenied, BackupMissing, InvalidStore,
    BadTarget, UnsupportedPlatform, DelayFailed, InvalidInstallation
}

internal sealed class StartupException(Problem problem, Exception? inner = null) : Exception(problem.ToString(), inner)
{
    internal Problem Problem { get; } = problem;
}

internal sealed record EntryId(StartupKind Kind, StartupScope Scope, string Site, string Name)
{
    public string Key => FormattableString.Invariant($"{Kind}|{Scope}|{Site}|{Name}");
}

internal sealed record StartupEntry(
    EntryId Id, string Name, string Location, string Command, string? Target, EntryState State,
    EntryActions Actions, string Revision, string Extra = "", bool WindowsComponent = false)
{
    public bool Active => State is EntryState.Active or EntryState.Manual;
}

internal sealed record Snapshot(
    StartupEntry Entry, string? RegistryType = null, string? BackupFile = null, string? BackupHash = null,
    string? TaskXml = null, string? TaskSddl = null, int StartType = 0, bool DelayedAuto = false,
    string? Definition = null, string? DelayTask = null, int DelaySeconds = 0,
    string? DelayFile = null, string? DelayArguments = null);

internal sealed record SavedEntry(Snapshot Original, SavedMode Mode, bool Guard, bool Pending = false);

internal sealed record Activity(DateTimeOffset When, ActivityKind Kind, string Entry, string Location, Problem? Problem = null);

internal sealed class StartupState
{
    public int Version { get; set; } = 1;
    public bool Paused { get; set; }
    public List<SavedEntry> Saved { get; set; } = [];
    public List<Activity> History { get; set; } = [];
}

internal sealed record ViewEntry(StartupEntry Entry, SavedEntry? Saved, bool HasCurrent, bool CanWrite);

internal sealed class Scan
{
    public List<StartupEntry> Entries { get; } = [];
    public HashSet<StartupKind> Unavailable { get; } = [];
}

internal static class Faults
{
    internal static bool Expected(Exception error) => error is StartupException or IOException
        or UnauthorizedAccessException or System.Security.SecurityException or System.Runtime.InteropServices.COMException
        or System.ComponentModel.Win32Exception or System.Xml.XmlException;

    internal static Problem From(Exception error) => error switch
    {
        StartupException known => known.Problem,
        UnauthorizedAccessException or System.Security.SecurityException => Problem.AccessDenied,
        System.Runtime.InteropServices.COMException { HResult: unchecked((int)0x80070005) } => Problem.AccessDenied,
        System.ComponentModel.Win32Exception { NativeErrorCode: 5 } => Problem.AccessDenied,
        _ => Problem.Unavailable
    };
}

