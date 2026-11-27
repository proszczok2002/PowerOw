namespace PowerOw;

internal sealed class StartupDiscovery
{
    private static readonly StartupKind[] Sources =
        [StartupKind.Registry, StartupKind.StartupFolder, StartupKind.Task, StartupKind.Service, StartupKind.Packaged];
    private readonly Dictionary<StartupKind, HashSet<string>> known = [];

    internal IReadOnlyList<StartupEntry> Observe(Scan scan)
    {
        var added = new List<StartupEntry>();
        var groups = scan.Entries.Where(entry => !entry.WindowsComponent).ToLookup(entry => entry.Id.Kind);
        foreach (var source in Sources)
        {
            // Retain the last complete snapshot if a provider is temporarily unavailable.
            if (scan.Unavailable.Contains(source))
                continue;
            var entries = groups[source].DistinctBy(entry => entry.Id.Key, StringComparer.OrdinalIgnoreCase).ToArray();
            if (known.TryGetValue(source, out var previous))
                added.AddRange(entries.Where(entry => !previous.Contains(entry.Id.Key)));
            known[source] = entries.Select(entry => entry.Id.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        return added;
    }
}
