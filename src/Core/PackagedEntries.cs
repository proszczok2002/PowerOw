using System.Security.Principal;
using System.Xml.Linq;
using Windows.Management.Deployment;

namespace PowerOw;

internal static class PackagedEntries
{
    internal static void Enumerate(Scan scan, string? desktopSid)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var packages = new PackageManager().FindPackagesForUser(desktopSid ?? identity.User?.Value ?? "");
            foreach (var package in packages)
            {
                try
                {
                    if (package.IsFramework || package.IsResourcePackage)
                        continue;
                    string directory = package.InstalledLocation.Path;
                    string manifestPath = Path.Combine(directory, "AppxManifest.xml");
                    if (!File.Exists(manifestPath))
                        continue;
                    using var manifest = File.OpenRead(manifestPath);
                    var document = XDocument.Load(manifest);
                    foreach (var node in document.Descendants().Where(item => item.Name.LocalName == "StartupTask"))
                    {
                        var extension = node.Parent;
                        if ((string?)extension?.Attribute("Category") != "windows.startupTask")
                            continue;
                        string? id = (string?)node.Attribute("TaskId");
                        if (string.IsNullOrWhiteSpace(id))
                            continue;
                        string? displayName = (string?)node.Attribute("DisplayName");
                        if (string.IsNullOrWhiteSpace(displayName) || displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                            displayName = id;
                        string fileName = (string?)extension?.Attribute("Executable") ?? "";
                        string? target = fileName.Length == 0 ? null : Path.GetFullPath(Path.Combine(directory, fileName));
                        if (target is not null && !target.StartsWith(Path.TrimEndingDirectorySeparator(directory) + "\\", StringComparison.OrdinalIgnoreCase))
                            target = null;
                        var key = new EntryId(StartupKind.Packaged, StartupScope.User, package.Id.FamilyName, id);
                        scan.Entries.Add(new StartupEntry(key, displayName, manifestPath, fileName, target,
                            EntryState.ReadOnly, EntryActions.None, key.Key, package.Id.FamilyName));
                    }
                }
                catch (Exception error) when (Faults.Expected(error) || error is ArgumentException) { scan.Unavailable.Add(StartupKind.Packaged); }
            }
        }
        catch (Exception error) when (Faults.Expected(error)) { scan.Unavailable.Add(StartupKind.Packaged); }
    }
}
