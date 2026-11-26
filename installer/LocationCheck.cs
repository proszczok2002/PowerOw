using System.Security.AccessControl;
using System.Security.Principal;

// Runs inside the elevated installer's private staging directory, before files are installed.
// A scheduled task and service may only use an installation path that unelevated processes cannot replace.
internal static class LocationCheck
{
    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier SystemAccount = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Installer = new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");
    private const int ReplaceDirectory = unchecked((int)0x500D0150); // replacement, permissions and reparse metadata
    private const int WriteFiles = 0x00000116; // FILE_WRITE_DATA, APPEND_DATA, WRITE_EA and WRITE_ATTRIBUTES

    private static int Main(string[] args)
    {
        if (args.Length != 1)
            return 2;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                return 5;
            string folder = Path.GetFullPath(args[0]).TrimEnd(Path.DirectorySeparatorChar);
            string volume = Path.GetPathRoot(folder);
            if (volume.Length != 3 || folder.Length <= volume.Length || folder.IndexOf(':', 2) >= 0 ||
                new DriveInfo(volume).DriveType != DriveType.Fixed)
                return 2;
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (folder.Equals(windows, StringComparison.OrdinalIgnoreCase) || folder.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase))
                return 2;
            string driveFormat = new DriveInfo(volume).DriveFormat;
            if (driveFormat != "NTFS" && driveFormat != "ReFS")
                return 2;
            var target = new DirectoryInfo(folder);
            var path = new Stack<DirectoryInfo>();
            for (DirectoryInfo? node = target; node is not null; node = node.Parent)
                path.Push(node);
            while (path.Count > 1)
            {
                var parent = path.Pop();
                if (!parent.Exists)
                {
                    parent.Create(ProtectedSecurity());
                    parent.Refresh();
                }
                if (!IsProtected(parent, ReplaceDirectory))
                    return 3;
            }
            if (File.Exists(folder))
                return 2;
            if (!target.Exists)
            {
                target.Create(ProtectedSecurity());
                target.Refresh();
            }
            if ((target.Attributes & FileAttributes.ReparsePoint) != 0)
                return 3;
            bool own = File.Exists(Path.Combine(folder, "installed.marker"));
            if (!own)
            {
                if (target.EnumerateFileSystemInfos().Any())
                    return 4;
                target.SetAccessControl(ProtectedSecurity());
                target.Refresh();
                if (target.EnumerateFileSystemInfos().Any())
                    return 4;
            }
            return IsProtected(target, ReplaceDirectory | WriteFiles) ? 0 : 3;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return 3;
        }
    }

    private static bool IsProtected(DirectoryInfo directory, int dangerous)
    {
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            return false;
        var binary = directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner).GetSecurityDescriptorBinaryForm();
        var descriptor = new RawSecurityDescriptor(binary, 0);
        return descriptor.Owner is { } owner && Trusted(owner) && descriptor.DiscretionaryAcl is { } acl &&
            !acl.OfType<QualifiedAce>().Any(ace => ace.AceQualifier == AceQualifier.AccessAllowed &&
                (ace.AceFlags & AceFlags.InheritOnly) == 0 && (ace.AccessMask & dangerous) != 0 && !Trusted(ace.SecurityIdentifier));
    }

    private static bool Trusted(SecurityIdentifier sid) => sid == Admins || sid == SystemAccount || sid == Installer;

    private static DirectorySecurity ProtectedSecurity()
    {
        var security = new DirectorySecurity();
        security.SetOwner(Admins);
        security.SetAccessRuleProtection(true, false);
        const InheritanceFlags descendants = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var sid in new[] { Admins, SystemAccount })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, descendants, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute, descendants, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }
}
