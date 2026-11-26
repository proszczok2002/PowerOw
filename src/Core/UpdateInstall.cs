using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PowerOw;

internal sealed class UpdateInstall : IDisposable
{
    private const string Prefix = "PowerOw-update-";
    private const string ConsentName = "PowerOw.UpdateConsent.exe";
    private static SecurityIdentifier Administrators => new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private readonly DirectoryInfo staging;

    internal UpdateInstall()
    {
        SafePaths.RequirePlain(InstallPaths.Root);
        staging = new DirectoryInfo(Path.Combine(InstallPaths.Root, Prefix + Guid.NewGuid().ToString("N")));
        var security = new DirectorySecurity();
        Grant(security, desktop: null, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);
        staging.Create(security);
        SafePaths.RequirePlain(staging.FullName);
    }

    internal string FilePath => Path.Combine(staging.FullName, Updates.InstallerName);

    internal Task<int> RequestConsent(Version version, string language, bool tray)
    {
        var properties = FileVersionInfo.GetVersionInfo(FilePath);
        if (properties.ProductName?.Trim() != "PowerOw" ||
            !Version.TryParse(properties.ProductVersion?.Trim(), out var reported) ||
            version != new Version(reported.Major, reported.Minor, Math.Max(0, reported.Build), 0))
            throw new InvalidDataException();
        string sid = AccountAccess.Sid ?? throw new StartupException(Problem.AccessDenied);
        var desktop = new SecurityIdentifier(sid);
        string source = Path.Combine(InstallPaths.Root, ConsentName);
        SafePaths.RequirePlain(source);
        string launcher = Path.Combine(staging.FullName, ConsentName);
        File.Copy(source, launcher);
        foreach (string path in new[] { launcher, FilePath })
        {
            var security = new FileSecurity();
            Grant(security, desktop, InheritanceFlags.None);
            new FileInfo(path).SetAccessControl(security);
        }
        var directorySecurity = new DirectorySecurity();
        Grant(directorySecurity, desktop, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);
        staging.SetAccessControl(directorySecurity);
        return AccountAccess.StartUpdateConsent(launcher, language == "zh-CN" ? "zh" : language, tray);
    }

    private static void Grant(FileSystemSecurity security, SecurityIdentifier? desktop, InheritanceFlags inheritance)
    {
        var admins = Administrators;
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.SetOwner(admins);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, inheritance,
            PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inheritance,
            PropagationFlags.None, AccessControlType.Allow));
        if (desktop is not null)
            security.AddAccessRule(new FileSystemAccessRule(desktop, FileSystemRights.ReadAndExecute, InheritanceFlags.None,
                PropagationFlags.None, AccessControlType.Allow));
    }

    internal static void ClearExpired()
    {
        try
        {
            foreach (var directory in new DirectoryInfo(InstallPaths.Root).EnumerateDirectories(Prefix + "*"))
                if (Guid.TryParseExact(directory.Name.AsSpan(Prefix.Length), "N", out _) &&
                    directory.CreationTimeUtc < DateTime.UtcNow.AddDays(-1))
                    Remove(directory.FullName);
        }
        catch (Exception error) when (Faults.Expected(error)) { }
    }

    internal static void RecordFailure(string phase, int code)
    {
        try
        {
            string folder = StateStore.Location(StartupScope.Machine);
            SafePaths.RequirePlain(folder);
            string log = Path.Combine(folder, "update-error.log");
            if (File.Exists(log))
                SafePaths.RequirePlain(log);
            File.WriteAllText(log, $"{DateTimeOffset.Now:O} {phase} 0x{code:X8}{Environment.NewLine}");
        }
        catch (Exception error) when (Faults.Expected(error)) { }
    }

    private static void Remove(string path)
    {
        try
        {
            SafePaths.RequirePlain(path);
            foreach (string name in new[] { Updates.InstallerName, ConsentName })
            {
                string file = Path.Combine(path, name);
                if (File.Exists(file))
                {
                    SafePaths.RequirePlain(file);
                    File.Delete(file);
                }
            }
            Directory.Delete(path);
        }
        catch (Exception error) when (Faults.Expected(error)) { }
    }

    public void Dispose() => Remove(staging.FullName);
}
