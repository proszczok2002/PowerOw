using System.Diagnostics;
using System.Text.Json;

namespace PowerOw;

internal sealed class StateStore(string directory)
{
    internal string DirectoryPath { get; } = Path.GetFullPath(directory);
    internal string Vault => Path.Combine(DirectoryPath, "Backups");
    private string FilePath => Path.Combine(DirectoryPath, "startup.json");

    internal static string Location(StartupScope scope) => scope == StartupScope.User
        ? Path.Combine(AccountAccess.Folder(Environment.SpecialFolder.LocalApplicationData), "PowerOw")
        : Path.Combine(InstallPaths.Root, "Data");

    internal T Locked<T>(Func<StartupState, T> readOrWrite)
    {
        if (!Directory.Exists(DirectoryPath))
            Directory.CreateDirectory(DirectoryPath);
        SafePaths.RequirePlain(DirectoryPath);
        using var lease = TakeLock();
        return readOrWrite(Read());
    }

    internal void Save(StartupState state)
    {
        string temporary = Path.Combine(DirectoryPath, $"{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    internal static void Log(StartupState state, ActivityKind kind, StartupEntry item, Problem? problem = null)
    {
        state.History.Insert(0, new Activity(DateTimeOffset.Now, kind, item.Name, item.Location, problem));
        if (state.History.Count > 500)
            state.History.RemoveRange(500, state.History.Count - 500);
    }

    private StartupState Read()
    {
        if (!File.Exists(FilePath))
            return new StartupState();
        try
        {
            SafePaths.RequirePlain(FilePath);
            using var stream = File.OpenRead(FilePath);
            if (stream.Length > 64L * 1024 * 1024)
                throw new StartupException(Problem.InvalidStore);
            var state = JsonSerializer.Deserialize<StartupState>(stream);
            if (state is null || state.Version != 1 || state.Saved is null || state.History is null ||
                state.Saved.Any(record => record?.Original?.Entry?.Id is null || !Enum.IsDefined(record.Mode)) ||
                state.Saved.Select(record => record.Original.Entry.Id.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Saved.Count)
                throw new StartupException(Problem.InvalidStore);
            return state;
        }
        catch (JsonException error) { throw new StartupException(Problem.InvalidStore, error); }
    }

    private FileStream TakeLock()
    {
        string path = Path.Combine(DirectoryPath, "access.lock");
        var watch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                if (File.Exists(path))
                    SafePaths.RequirePlain(path);
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            }
            catch (IOException error) when ((error.HResult & 0xFFFF) == 32 && watch.Elapsed < TimeSpan.FromSeconds(3))
            {
                Thread.Sleep(60);
            }
        }
    }
}

internal static class SafePaths
{
    internal static void RequirePlain(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new StartupException(Problem.BadTarget);
        }
    }

    internal static string Child(string parent, string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.EndsWith(' ') || name.EndsWith('.'))
            throw new StartupException(Problem.BadTarget);
        string result = Path.GetFullPath(Path.Combine(parent, name));
        if (!string.Equals(Path.GetDirectoryName(result), Path.TrimEndingDirectorySeparator(parent), StringComparison.OrdinalIgnoreCase))
            throw new StartupException(Problem.BadTarget);
        return result;
    }
}

internal static class InstallPaths
{
    internal const string ServiceName = "PowerOwGuard";
    internal const string RegistryPath = @"Software\Proszczok\PowerOw";
    internal static string Root
    {
        get
        {
            using var key = RegistryEntries.Open(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64, RegistryPath);
            string? directory = key?.GetValue("Directory") as string;
            return CommandPath.IsLocalPath(directory) ? Path.GetFullPath(directory!) :
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerOw");
        }
    }
    internal static bool IsInstalled => string.Equals(Environment.ProcessPath, Path.Combine(Root, "PowerOw.exe"), StringComparison.OrdinalIgnoreCase)
        && File.Exists(Path.Combine(Root, "installed.marker"));
}
