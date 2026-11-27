using Microsoft.Win32;

namespace PowerOw;

internal sealed record Inventory(IReadOnlyList<ViewEntry> Items, IReadOnlyCollection<StartupKind> Unavailable, StartupState State,
    Problem? StateProblem);

internal sealed class StartupEngine(StartupScope scope, StateStore store, StartupEngine? account = null, bool applicationsOnly = false)
{
    internal StartupScope Scope { get; } = scope;
    private bool IsUser => Scope == StartupScope.User;

    internal Inventory Load()
    {
        var scan = ScanEntries();
        var (state, problem) = ReadState();
        Problem? accountProblem = null;
        if (account is not null)
        {
            var (userState, userProblem) = AccountAccess.Run(account.ReadState);
            accountProblem = userProblem;
            state.Saved.AddRange(userState.Saved);
            state.History = state.History.Concat(userState.History).OrderByDescending(activity => activity.When).Take(500).ToList();
        }
        var saved = state.Saved.ToDictionary(record => record.Original.Entry.Id.Key, StringComparer.OrdinalIgnoreCase);
        var rows = new List<ViewEntry>(scan.Entries.Count + saved.Count);
        foreach (var entry in scan.Entries.DistinctBy(entry => entry.Id.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (applicationsOnly && entry.WindowsComponent)
                continue;
            saved.Remove(entry.Id.Key, out var record);
            var stateAware = record is not null && !entry.Active
                ? entry with { State = StateFor(record) } : entry;
            rows.Add(new ViewEntry(stateAware, record, true, Writable(entry.Id.Scope) && entry.Actions != EntryActions.None));
        }
        foreach (var record in saved.Values)
        {
            var entry = record.Original.Entry with { State = StateFor(record) };
            rows.Add(new ViewEntry(entry, record, false, Writable(entry.Id.Scope)));
        }
        return new Inventory(rows, scan.Unavailable, state, problem ?? accountProblem);

        bool Writable(StartupScope entryScope) => entryScope == Scope ? problem is null :
            entryScope == StartupScope.User && account is not null && accountProblem is null;
    }

    private (StartupState, Problem?) ReadState()
    {
        try { return (store.Locked(current => current), null); }
        catch (Exception error) when (Faults.Expected(error)) { return (new StartupState(), Faults.From(error)); }
    }

    internal Scan ScanStartupApplications() => ScanEntries(startupOnly: true);

    private Scan ScanEntries(bool startupOnly = false)
    {
        var scan = new Scan();
        bool apps = applicationsOnly || startupOnly;
        RegistryEntries.Enumerate(scan, IsUser);
        StartupFolders.Enumerate(scan, IsUser);
        Tasks.Enumerate(scan, apps, startupOnly);
        Services.Enumerate(scan, apps, startupOnly);
        if (!apps)
            ExtendedEntries.Registry(scan, IsUser, profiles: !IsUser);
        if (!IsUser)
        {
            if (!apps)
                ExtendedEntries.Wmi(scan);
            PackagedEntries.Enumerate(scan, AccountAccess.Sid);
        }
        if (account is not null)
        {
            AccountAccess.Run(() =>
            {
                RegistryEntries.Enumerate(scan, includeUser: true, onlyUser: true);
                StartupFolders.Enumerate(scan, includeUser: true, onlyUser: true);
                if (!apps)
                    ExtendedEntries.Registry(scan, includeUser: true, profiles: false, onlyUser: true);
                return true;
            });
        }
        return scan;
    }

    internal void RecordDiscoveries(IReadOnlyList<StartupEntry> entries)
    {
        if (entries.Count == 0)
            return;
        if (account is not null)
        {
            AccountAccess.Run(() => { account.RecordDiscoveries(entries); return true; });
            return;
        }
        store.Locked(state =>
        {
            foreach (var entry in entries)
                StateStore.Log(state, ActivityKind.Detected, entry);
            store.Save(state);
            return true;
        });
    }

    private static EntryState StateFor(SavedEntry saved) => saved.Mode switch
    {
        SavedMode.Disabled => EntryState.Disabled,
        SavedMode.Removed => EntryState.Removed,
        _ => EntryState.Delayed
    };

    internal void Apply(ViewEntry selected, Change change, int delaySeconds = 0)
    {
        var id = selected.Entry.Id;
        if (account is not null && id.Scope == StartupScope.User)
        {
            AccountAccess.Run(() => { account.Apply(selected, change, delaySeconds); return true; });
            return;
        }
        if (id.Scope != Scope || !selected.CanWrite)
            throw new StartupException(Problem.ReadOnly);
        store.Locked(state =>
        {
            var saved = Find(state, id);
            if (saved != selected.Saved)
                throw new StartupException(Problem.Changed);
            var current = Read(id);
            if (selected.HasCurrent != (current is not null) || (current is not null && current.Revision != selected.Entry.Revision))
                throw new StartupException(Problem.Changed);
            switch (change)
            {
                case Change.Enable when saved is null && current is { Id.Kind: StartupKind.Task, State: EntryState.WindowsDisabled }
                    && current.Actions.HasFlag(EntryActions.Enable):
                    Tasks.Enable(current);
                    StateStore.Log(state, ActivityKind.Reenabled, current);
                    store.Save(state);
                    break;
                case Change.Restore when saved is not null:
                    Restore(state, saved, current);
                    break;
                case Change.StopGuarding when saved is { Guard: true }:
                    Replace(state, saved, saved with { Guard = false });
                    StateStore.Log(state, ActivityKind.RuleRemoved, saved.Original.Entry);
                    store.Save(state);
                    break;
                case Change.ForgetBackup when saved is { Guard: false, Mode: not SavedMode.Delayed }:
                    state.Saved.Remove(saved);
                    store.Save(state);
                    Discard(saved.Original);
                    break;
                case Change.Delay when saved is null && current is not null && IsUser:
                    Delay(state, current, delaySeconds);
                    break;
                case Change.Disable or Change.Remove or Change.DisableAndGuard or Change.RemoveAndGuard:
                    TurnOff(state, current, saved, change);
                    break;
                default:
                    throw new StartupException(Problem.ReadOnly);
            }
            return true;
        });
    }

    internal void SetPaused(bool paused) => store.Locked(state =>
    {
        state.Paused = paused;
        store.Save(state);
        return true;
    });

    internal bool ReadPaused() => store.Locked(state => state.Paused);

    internal int GuardAccount() => account is not null ? store.Locked(state => state.Paused
        ? 0 : AccountAccess.Run(() => account.GuardTick(obeyScopePause: false))) : 0;

    internal int GuardTick(bool obeyScopePause = true) => store.Locked(state =>
    {
        if (obeyScopePause && state.Paused)
            return 0;
        int blocked = 0;
        bool dirty = false;
        foreach (var saved in state.Saved.Where(record => record.Guard || record.Pending).ToArray())
        {
            StartupEntry original = saved.Original.Entry;
            if (original.Id.Scope != Scope)
                continue;
            try
            {
                var current = Read(original.Id, lightweight: true);
                bool remove = saved.Mode == SavedMode.Removed;
                bool shouldChange = current is not null && NeedsChange(current, remove);
                if (saved.Pending)
                {
                    if (shouldChange && current!.Revision != original.Revision && !saved.Guard)
                        throw new StartupException(Problem.Changed);
                    if (saved.Mode == SavedMode.Delayed)
                        FinishDelay(saved.Original, current);
                    else if (shouldChange)
                        Mutate(current!, remove);
                    Replace(state, saved, saved with { Pending = false });
                    StateStore.Log(state, ActivityFor(saved.Mode), original);
                    dirty = true;
                }
                else if (shouldChange)
                {
                    Mutate(current!, remove);
                    StateStore.Log(state, ActivityKind.Blocked, current!);
                    blocked++;
                    dirty = true;
                }
            }
            catch (Exception error) when (Faults.Expected(error))
            {
                var reason = Faults.From(error);
                if (!state.History.Take(50).Any(entry => entry.Kind == ActivityKind.Failed && entry.Entry == original.Name
                    && entry.Location == original.Location && entry.Problem == reason && entry.When > DateTimeOffset.Now.AddMinutes(-2)))
                {
                    StateStore.Log(state, ActivityKind.Failed, original, reason);
                    dirty = true;
                }
            }
        }
        if (dirty)
            store.Save(state);
        return blocked;
    });

    private void TurnOff(StartupState state, StartupEntry? current, SavedEntry? saved, Change change)
    {
        if (saved is { Mode: SavedMode.Delayed })
            throw new StartupException(Problem.ReadOnly);
        bool remove = change is Change.Remove or Change.RemoveAndGuard;
        bool guard = change is Change.DisableAndGuard or Change.RemoveAndGuard || saved?.Guard == true;
        var mode = remove ? SavedMode.Removed : SavedMode.Disabled;
        if (current is null && saved is null)
            throw new StartupException(Problem.Changed);
        bool upgrade = remove && current is { Id.Kind: StartupKind.Task } && saved?.Original.TaskXml is null;
        var original = saved is not null && !upgrade ? saved.Original : Capture(current!, remove);
        if (upgrade && saved is not null)
        {
            if (saved.Original.Definition != original.Definition)
                throw new StartupException(Problem.Changed);
            original = original with { Entry = saved.Original.Entry };
        }
        var pending = new SavedEntry(original, mode, guard, Pending: true);
        Replace(state, saved, pending);
        store.Save(state);
        try
        {
            if (current is not null && NeedsChange(current, remove))
                Mutate(current, remove);
        }
        catch (Exception error) when (Faults.Expected(error))
        {
            Replace(state, pending, saved);
            StateStore.Log(state, ActivityKind.Failed, original.Entry, Faults.From(error));
            store.Save(state);
            if (saved is null)
                Discard(original);
            throw;
        }
        Replace(state, pending, pending with { Pending = false });
        StateStore.Log(state, ActivityFor(mode), original.Entry);
        store.Save(state);
    }

    private void Delay(StartupState state, StartupEntry current, int seconds)
    {
        if (!current.Actions.HasFlag(EntryActions.Delay) || current.State != EntryState.Active || seconds is < 5 or > 600)
            throw new StartupException(Problem.BadTarget);
        var original = RegistryEntries.Capture(current);
        var target = CommandPath.CanDelay(current.Command, Enum.Parse<RegistryValueKind>(original.RegistryType!))
            ?? throw new StartupException(Problem.BadTarget);
        var snapshot = original with { DelayTask = Tasks.NewDelayPath(), DelaySeconds = seconds,
            DelayFile = target.File, DelayArguments = target.Arguments };
        var pending = new SavedEntry(snapshot, SavedMode.Delayed, Guard: true, Pending: true);
        state.Saved.Add(pending);
        store.Save(state);
        try
        {
            Tasks.CreateDelay(snapshot);
            RegistryEntries.Remove(current);
            Tasks.SetDelayEnabled(snapshot, true);
        }
        catch (Exception error) when (Faults.Expected(error))
        {
            try
            {
                Tasks.DeleteDelay(snapshot);
                if (Read(current.Id) is null)
                    RegistryEntries.Restore(snapshot);
                state.Saved.Remove(pending);
                StateStore.Log(state, ActivityKind.Failed, current, Faults.From(error));
                store.Save(state);
            }
            catch (Exception recovery) when (Faults.Expected(recovery))
            {
                throw new StartupException(Problem.DelayFailed, recovery);
            }
            throw;
        }
        Replace(state, pending, pending with { Pending = false });
        StateStore.Log(state, ActivityKind.Delayed, current);
        store.Save(state);
    }

    private void FinishDelay(Snapshot snapshot, StartupEntry? current)
    {
        if (current is not null)
        {
            if (current.Revision != snapshot.Entry.Revision)
                throw new StartupException(Problem.Changed);
            try { Tasks.SetDelayEnabled(snapshot, false); }
            catch (StartupException error) when (error.Problem == Problem.DelayFailed) { Tasks.CreateDelay(snapshot); }
            RegistryEntries.Remove(current);
        }
        Tasks.SetDelayEnabled(snapshot, true);
    }

    private void Restore(StartupState state, SavedEntry saved, StartupEntry? current)
    {
        var original = saved.Original;
        var off = saved with { Guard = false, Pending = false };
        Replace(state, saved, off);
        store.Save(state);
        try
        {
            switch (original.Entry.Id.Kind)
            {
                case StartupKind.Registry:
                    if (saved.Mode == SavedMode.Delayed)
                        Tasks.DeleteDelay(original);
                    if (current is null)
                        RegistryEntries.Restore(original);
                    else if (current.Revision != original.Entry.Revision)
                        throw new StartupException(Problem.Occupied);
                    break;
                case StartupKind.StartupFolder when current is null:
                    StartupFolders.Restore(original, store.Vault);
                    break;
                case StartupKind.StartupFolder:
                    if (current!.Revision != original.Entry.Revision || !StartupFolders.MatchesBackup(original))
                        throw new StartupException(Problem.Occupied);
                    break;
                case StartupKind.Task:
                    Tasks.Restore(original, current);
                    break;
                case StartupKind.Service when current is not null:
                    Services.Restore(original, current);
                    break;
                default:
                    throw new StartupException(Problem.BackupMissing);
            }
        }
        catch (Exception error) when (Faults.Expected(error))
        {
            StateStore.Log(state, ActivityKind.Failed, original.Entry, Faults.From(error));
            store.Save(state);
            throw;
        }
        state.Saved.Remove(off);
        StateStore.Log(state, ActivityKind.Reenabled, original.Entry);
        store.Save(state);
        Discard(original);
    }

    private static bool NeedsChange(StartupEntry entry, bool delete) =>
        entry.Id.Kind is StartupKind.Registry or StartupKind.StartupFolder || delete || entry.Active;

    private static void Mutate(StartupEntry current, bool delete)
    {
        if (!current.Actions.HasFlag(delete ? EntryActions.Remove : EntryActions.Disable))
            throw new StartupException(Problem.ReadOnly);
        switch (current.Id.Kind)
        {
            case StartupKind.Registry: RegistryEntries.Remove(current); break;
            case StartupKind.StartupFolder: StartupFolders.Remove(current); break;
            case StartupKind.Task: Tasks.Change(current, delete); break;
            case StartupKind.Service when !delete: Services.Disable(current); break;
            default: throw new StartupException(Problem.ReadOnly);
        }
    }

    private Snapshot Capture(StartupEntry current, bool delete)
    {
        if (!current.Actions.HasFlag(delete ? EntryActions.Remove : EntryActions.Disable))
            throw new StartupException(Problem.ReadOnly);
        return current.Id.Kind switch
        {
            StartupKind.Registry => RegistryEntries.Capture(current),
            StartupKind.StartupFolder => StartupFolders.Capture(current, store.Vault),
            StartupKind.Task => Tasks.Capture(current, delete),
            StartupKind.Service when !delete => Services.Capture(current),
            _ => throw new StartupException(Problem.ReadOnly)
        };
    }

    private StartupEntry? Read(EntryId id, bool lightweight = false) => id.Kind switch
    {
        StartupKind.Registry => RegistryEntries.Read(id, IsUser && !lightweight),
        StartupKind.StartupFolder => StartupFolders.Read(id, IsUser, describe: !lightweight),
        StartupKind.Task => Tasks.Read(id),
        StartupKind.Service => Services.Read(id),
        _ => throw new StartupException(Problem.ReadOnly)
    };

    private void Discard(Snapshot original)
    {
        try { StartupFolders.DiscardBackup(original, store.Vault); }
        catch (Exception error) when (Faults.Expected(error)) { }
    }

    private static SavedEntry? Find(StartupState state, EntryId id) =>
        state.Saved.Find(record => string.Equals(record.Original.Entry.Id.Key, id.Key, StringComparison.OrdinalIgnoreCase));

    private static void Replace(StartupState state, SavedEntry? old, SavedEntry? current)
    {
        if (old is not null)
            state.Saved.Remove(old);
        if (current is not null)
            state.Saved.Add(current);
    }

    private static ActivityKind ActivityFor(SavedMode mode) => mode switch
    {
        SavedMode.Disabled => ActivityKind.Disabled,
        SavedMode.Removed => ActivityKind.Removed,
        _ => ActivityKind.Delayed
    };
}
