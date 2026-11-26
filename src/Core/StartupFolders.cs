using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;

namespace PowerOw;

internal static class StartupFolders
{
    internal static void Enumerate(Scan scan, bool includeUser, bool onlyUser = false)
    {
        foreach (var scope in Enum.GetValues<StartupScope>().Where(scope => onlyUser ? scope == StartupScope.User : includeUser || scope == StartupScope.Machine))
        {
            try
            {
                string directory = DirectoryFor(scope);
                if (!Directory.Exists(directory))
                    continue;
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    var id = new EntryId(StartupKind.StartupFolder, scope, "", Path.GetFileName(file));
                    var item = Read(id, includeUser, describe: true);
                    if (item is not null)
                        scan.Entries.Add(item);
                }
            }
            catch (Exception error) when (Faults.Expected(error)) { scan.Unavailable.Add(StartupKind.StartupFolder); }
        }
    }

    internal static StartupEntry? Read(EntryId id, bool userApproval, bool describe)
    {
        if (id.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            return null;
        string file = NameFor(id);
        var info = new FileInfo(file);
        if (!info.Exists)
            return null;
        bool link = (info.Attributes & FileAttributes.ReparsePoint) != 0;
        var actions = link ? EntryActions.None : EntryActions.Disable | EntryActions.Remove;
        string command = file;
        string? target = file;
        if (describe && !link && info.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var shortcut = ReadShortcut(file);
            if (shortcut.Path.Length > 0)
            {
                target = CommandPath.IsLocalPath(shortcut.Path) ? shortcut.Path : null;
                command = $"\"{shortcut.Path}\" {shortcut.Arguments}".TrimEnd();
            }
            else
                target = null;
        }
        bool disabled = describe && RegistryEntries.IsWindowsDisabled(id.Scope, "StartupFolder", id.Name, userApproval);
        return new StartupEntry(id, id.Name, file, command, target,
            disabled ? EntryState.WindowsDisabled : EntryState.Active, actions,
            FormattableString.Invariant($"{info.Length}|{info.LastWriteTimeUtc.Ticks}|{(int)info.Attributes}"));
    }

    internal static Snapshot Capture(StartupEntry item, string vault)
    {
        string original = MutablePath(item.Id);
        Directory.CreateDirectory(vault);
        SafePaths.RequirePlain(vault);
        string id = $"{Guid.NewGuid():N}.bin";
        string backup = SafePaths.Child(vault, id);
        try
        {
            if (Read(item.Id, false, false)?.Revision != item.Revision)
                throw new StartupException(Problem.Changed);
            File.Copy(original, backup);
            if (Read(item.Id, false, false)?.Revision != item.Revision)
                throw new StartupException(Problem.Changed);
            return new Snapshot(item, BackupFile: id, BackupHash: Hash(backup));
        }
        catch
        {
            DeleteReadOnly(backup);
            throw;
        }
    }

    internal static void Remove(StartupEntry current)
    {
        string file = MutablePath(current.Id);
        if (Read(current.Id, false, false)?.Revision != current.Revision)
            throw new StartupException(Problem.Changed);
        File.Delete(file);
    }

    internal static void Restore(Snapshot snapshot, string vault)
    {
        string folder = DirectoryFor(snapshot.Entry.Id.Scope);
        SafePaths.RequirePlain(folder);
        string file = NameFor(snapshot.Entry.Id);
        if (File.Exists(file) || Directory.Exists(file))
            throw new StartupException(Problem.Occupied);
        string backup = FileForBackup(snapshot, vault);
        if (!File.Exists(backup) || Hash(backup) != snapshot.BackupHash)
            throw new StartupException(Problem.BackupMissing);
        string temporary = SafePaths.Child(folder, $"PowerOw-{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(backup, temporary);
            File.Move(temporary, file);
        }
        finally { DeleteReadOnly(temporary); }
    }

    internal static void DiscardBackup(Snapshot snapshot, string vault)
    {
        if (snapshot.BackupFile is not null)
            DeleteReadOnly(FileForBackup(snapshot, vault));
    }

    internal static bool MatchesBackup(Snapshot snapshot)
    {
        string file = MutablePath(snapshot.Entry.Id);
        return Hash(file) == snapshot.BackupHash;
    }

    private static string FileForBackup(Snapshot snapshot, string vault)
    {
        if (snapshot.BackupFile is not { Length: 36 } name || !name.EndsWith(".bin", StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[..32], "N", out _))
            throw new StartupException(Problem.InvalidStore);
        SafePaths.RequirePlain(vault);
        string path = SafePaths.Child(vault, name);
        if (File.Exists(path))
            SafePaths.RequirePlain(path);
        return path;
    }

    internal static void DeleteReadOnly(string path)
    {
        if (!File.Exists(path))
            return;
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new StartupException(Problem.BadTarget);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        try { File.Delete(path); }
        catch
        {
            if ((attributes & FileAttributes.ReadOnly) != 0 && File.Exists(path))
                File.SetAttributes(path, attributes);
            throw;
        }
    }

    private static string Hash(string file)
    {
        using var source = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(source));
    }

    private static string MutablePath(EntryId id)
    {
        string file = NameFor(id);
        SafePaths.RequirePlain(file);
        return file;
    }

    private static string NameFor(EntryId id)
    {
        if (id.Site.Length != 0 || id.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            throw new StartupException(Problem.BadTarget);
        return SafePaths.Child(DirectoryFor(id.Scope), id.Name);
    }

    private static string DirectoryFor(StartupScope scope)
    {
        string directory = scope == StartupScope.User ? AccountAccess.Folder(Environment.SpecialFolder.Startup) :
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup, Environment.SpecialFolderOption.DoNotVerify);
        return CommandPath.IsLocalPath(directory) ? Path.TrimEndingDirectorySeparator(directory)
            : throw new StartupException(Problem.BadTarget);
    }

    private static (string Path, string Arguments) ReadShortcut(string file)
    {
        object? shortcut = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), throwOnError: true)!;
            shortcut = Activator.CreateInstance(type)!;
            ((IPersistFile)shortcut).Load(file, 0);
            var link = (IShellLink)shortcut;
            var path = new StringBuilder(32768);
            var args = new StringBuilder(32768);
            link.GetPath(path, path.Capacity, IntPtr.Zero, 4); // SLGP_RAWPATH, without resolving or launching the shortcut.
            link.GetArguments(args, args.Capacity);
            return (Environment.ExpandEnvironmentVariables(path.ToString()), args.ToString());
        }
        catch (COMException) { return ("", ""); }
        finally
        {
            if (shortcut is not null)
                Marshal.ReleaseComObject(shortcut);
        }
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, IntPtr findData, uint flags);
        void GetIdList(out IntPtr idList);
        void SetIdList(IntPtr idList);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int max);
    }
}
