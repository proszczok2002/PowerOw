using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;

namespace PowerOw;

internal static class Tasks
{
    private const int NotFound = unchecked((int)0x80070002);
    private const int PathNotFound = unchecked((int)0x80070003);
    private const int CreateWithoutRegistrationTrigger = 2 | 32;
    private const string DelayPrefix = @"\PowerOw.Delay.";
    private const string LoginPath = @"\PowerOw";
    private const string LoginMarker = "Proszczok.PowerOw.Logon";
    private const string AdminGroup = "S-1-5-32-544";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private static EntryId LoginId => new(StartupKind.Task, StartupScope.Machine, "", LoginPath);

    internal static bool LogonEnabled()
    {
        using var task = Find(LoginId);
        if (task is null)
            return false;
        VerifyLogonTask(task);
        return task.Api.Enabled;
    }

    internal static void EnsureLogonTask()
    {
        using var existing = Find(LoginId);
        if (existing is not null)
        {
            VerifyLogonTask(existing);
            return;
        }
        string executable = Path.Combine(InstallPaths.Root, "PowerOw.exe");
        XElement Element(string name, object body) => new(Ns + name, body);
        var xml = new XElement(Ns + "Task", new XAttribute("version", "1.2"),
            Element("RegistrationInfo", Element("Source", LoginMarker)),
            Element("Triggers", Element("LogonTrigger", new[] { Element("Enabled", "true"), Element("Delay", "PT3S") })),
            Element("Principals", Element("Principal", new object[]
            {
                new XAttribute("id", "PowerOw"), Element("GroupId", AdminGroup), Element("RunLevel", "HighestAvailable")
            })),
            Element("Settings", new[]
            {
                Element("Enabled", "true"), Element("MultipleInstancesPolicy", "Parallel"),
                Element("DisallowStartIfOnBatteries", "false"), Element("StopIfGoingOnBatteries", "false"),
                Element("ExecutionTimeLimit", "PT0S")
            }),
            Element("Actions", new object[]
            {
                new XAttribute("Context", "PowerOw"),
                Element("Exec", new[] { Element("Command", executable), Element("Arguments", "--tray"), Element("WorkingDirectory", InstallPaths.Root) })
            }));
        using var scheduler = Connect();
        using var folder = new ComObject(scheduler.Api.GetFolder(@"\"));
        using var task = new ComObject(folder.Api.RegisterTask(LoginPath[1..], xml.ToString(SaveOptions.DisableFormatting),
            CreateWithoutRegistrationTrigger, AdminGroup, null, 4, "D:P(A;;FA;;;SY)(A;;FA;;;BA)"));
        VerifyLogonTask(task);
    }

    internal static void SetLogonEnabled(bool enabled)
    {
        if (enabled)
            EnsureLogonTask();
        using var task = Find(LoginId);
        if (task is not null)
        {
            VerifyLogonTask(task);
            task.Api.Enabled = enabled;
        }
    }

    internal static void RemoveLogonTask()
    {
        using var task = Find(LoginId);
        if (task is null)
            return;
        VerifyLogonTask(task);
        using var scheduler = Connect();
        using var folder = new ComObject(scheduler.Api.GetFolder(@"\"));
        folder.Api.DeleteTask(LoginPath[1..], 0);
    }

    private static void VerifyLogonTask(ComObject task)
    {
        XElement xml = XElement.Parse((string)task.Api.Xml);
        var actions = xml.Element(Ns + "Actions")?.Elements().ToArray();
        var exec = actions is { Length: 1 } ? actions[0] : null;
        if ((string?)xml.Element(Ns + "RegistrationInfo")?.Element(Ns + "Source") != LoginMarker ||
            !string.Equals((string?)exec?.Element(Ns + "Command"), Path.Combine(InstallPaths.Root, "PowerOw.exe"), StringComparison.OrdinalIgnoreCase) ||
            (string?)exec?.Element(Ns + "Arguments") != "--tray")
            throw new StartupException(Problem.Occupied);
    }

    internal static void Enumerate(Scan scan, bool applicationsOnly = false)
    {
        try
        {
            using var scheduler = Connect();
            using var root = new ComObject(scheduler.Api.GetFolder(@"\"));
            Visit(root);
        }
        catch (Exception error) when (Faults.Expected(error)) { scan.Unavailable.Add(StartupKind.Task); }

        void Visit(ComObject folder)
        {
            try
            {
                using var tasks = new ComObject(folder.Api.GetTasks(1));
                for (int index = 1; index <= (int)tasks.Api.Count; index++)
                {
                    try
                    {
                        using var task = new ComObject(tasks.Api.Item(index));
                        if (applicationsOnly && Protected((string)task.Api.Path))
                            continue;
                        var details = Describe(task);
                        if (!details.Entry.Id.Name.StartsWith(DelayPrefix, StringComparison.OrdinalIgnoreCase))
                            scan.Entries.Add(details.Entry);
                    }
                    catch (Exception error) when (Faults.Expected(error)) { scan.Unavailable.Add(StartupKind.Task); }
                }
            }
            catch (Exception error) when (Faults.Expected(error)) { scan.Unavailable.Add(StartupKind.Task); }
            try
            {
                using var folders = new ComObject(folder.Api.GetFolders(0));
                for (int index = 1; index <= (int)folders.Api.Count; index++)
                {
                    using var child = new ComObject(folders.Api.Item(index));
                    if (applicationsOnly && ((string)child.Api.Path).Equals(@"\Microsoft\Windows", StringComparison.OrdinalIgnoreCase))
                        continue;
                    Visit(child);
                }
            }
            catch (Exception error) when (Faults.Expected(error)) { scan.Unavailable.Add(StartupKind.Task); }
        }
    }

    internal static StartupEntry? Read(EntryId id)
    {
        using var task = Find(id);
        return task is null ? null : Describe(task).Entry;
    }

    internal static Snapshot Capture(StartupEntry current, bool deleting)
    {
        using var task = Find(current.Id) ?? throw new StartupException(Problem.Changed);
        var details = Require(task, current);
        if (deleting && !details.Entry.Actions.HasFlag(EntryActions.Remove))
            throw new StartupException(Problem.ReadOnly);
        string? sddl = deleting ? (string)task.Api.GetSecurityDescriptor(7) : null; // owner, group and DACL
        return new Snapshot(current, TaskXml: deleting ? details.Xml : null, TaskSddl: sddl, Definition: details.Definition);
    }

    internal static void Change(StartupEntry current, bool delete)
    {
        using var scheduler = Connect();
        var (directory, name) = Parts(current.Id);
        using var folder = new ComObject(scheduler.Api.GetFolder(directory));
        using var task = new ComObject(folder.Api.GetTask(name));
        var details = Require(task, current);
        if (delete)
        {
            if (!details.Entry.Actions.HasFlag(EntryActions.Remove))
                throw new StartupException(Problem.ReadOnly);
            folder.Api.DeleteTask(name, 0);
        }
        else
            task.Api.Enabled = false;
    }

    internal static void Enable(StartupEntry current)
    {
        using var task = Find(current.Id) ?? throw new StartupException(Problem.Changed);
        Require(task, current);
        task.Api.Enabled = true;
    }

    internal static void Restore(Snapshot original, StartupEntry? current)
    {
        if (current is not null)
        {
            using var task = Find(current.Id) ?? throw new StartupException(Problem.Changed);
            var live = Require(task, current);
            if (live.Definition != original.Definition)
                throw new StartupException(Problem.Occupied);
            task.Api.Enabled = original.Entry.State != EntryState.WindowsDisabled;
            return;
        }
        if (original.TaskXml is null)
            throw new StartupException(Problem.BackupMissing);
        var (directory, name) = Parts(original.Entry.Id);
        if (Protected(original.Entry.Id.Name))
            throw new StartupException(Problem.ReadOnly);
        XElement xml = XElement.Parse(original.TaskXml);
        var principal = xml.Element(Ns + "Principals")?.Element(Ns + "Principal");
        int logon = Logon(principal);
        string? user = (string?)principal?.Element(Ns + (logon == 4 ? "GroupId" : "UserId"));
        if (!CanRecreate(logon) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(original.TaskSddl))
            throw new StartupException(Problem.BackupMissing);
        if (xml.Element(Ns + "Settings") is { } settings)
            settings.SetElementValue(Ns + "Enabled", original.Entry.State != EntryState.WindowsDisabled);
        using var scheduler = Connect();
        using var folder = new ComObject(scheduler.Api.GetFolder(directory));
        using var registered = new ComObject(folder.Api.RegisterTask(name, xml.ToString(SaveOptions.DisableFormatting),
            CreateWithoutRegistrationTrigger, user, null, logon, original.TaskSddl));
    }

    internal static string NewDelayPath() => DelayPrefix + Guid.NewGuid().ToString("N");

    internal static void CreateDelay(Snapshot snapshot)
    {
        var id = DelayId(snapshot);
        string file = snapshot.DelayFile ?? throw new StartupException(Problem.BadTarget);
        if (!File.Exists(file))
            throw new StartupException(Problem.BadTarget);
        using var identity = WindowsIdentity.GetCurrent();
        string sid = identity.User!.Value;
        XElement Element(string name, object content) => new(Ns + name, content);
        var taskXml = new XElement(Ns + "Task", new XAttribute("version", "1.2"),
            Element("RegistrationInfo", Element("Source", id.Name)),
            Element("Triggers", Element("LogonTrigger", new[]
            {
                Element("Enabled", "true"),
                Element("UserId", sid),
                Element("Delay", "PT" + snapshot.DelaySeconds.ToString(CultureInfo.InvariantCulture) + "S"),
            })),
            Element("Principals", Element("Principal", new object[]
            {
                new XAttribute("id", "PowerOw"), Element("UserId", sid), Element("LogonType", "InteractiveToken"),
                Element("RunLevel", "LeastPrivilege")
            })),
            Element("Settings", new[]
            {
                Element("DisallowStartIfOnBatteries", "false"), Element("StopIfGoingOnBatteries", "false"),
                Element("Enabled", "false"), Element("ExecutionTimeLimit", "PT0S")
            }),
            Element("Actions", new object[]
            {
                new XAttribute("Context", "PowerOw"), Element("Exec", new[]
                {
                    Element("Command", file), Element("Arguments", snapshot.DelayArguments ?? ""),
                    Element("WorkingDirectory", Path.GetDirectoryName(file)!)
                })
            }));
        using var scheduler = Connect();
        using var folder = new ComObject(scheduler.Api.GetFolder(@"\"));
        using var task = new ComObject(folder.Api.RegisterTask(id.Name[1..], taskXml.ToString(SaveOptions.DisableFormatting),
            CreateWithoutRegistrationTrigger, sid, null, 3, null));
    }

    internal static void SetDelayEnabled(Snapshot snapshot, bool enabled)
    {
        var id = DelayId(snapshot);
        using var task = Find(id) ?? throw new StartupException(Problem.DelayFailed);
        RequireDelay(task, id, snapshot);
        task.Api.Enabled = enabled;
    }

    internal static void DeleteDelay(Snapshot snapshot)
    {
        var id = DelayId(snapshot);
        using var task = Find(id);
        if (task is null)
            return;
        RequireDelay(task, id, snapshot);
        using var scheduler = Connect();
        using var folder = new ComObject(scheduler.Api.GetFolder(@"\"));
        folder.Api.DeleteTask(id.Name[1..], 0);
    }

    private static void RequireDelay(ComObject task, EntryId id, Snapshot snapshot)
    {
        XElement xml = XElement.Parse((string)task.Api.Xml);
        var actions = xml.Element(Ns + "Actions")?.Elements().ToArray();
        var exec = actions is { Length: 1 } ? actions[0] : null;
        if ((string?)xml.Element(Ns + "RegistrationInfo")?.Element(Ns + "Source") != id.Name ||
            (string?)exec?.Element(Ns + "Command") != snapshot.DelayFile ||
            ((string?)exec?.Element(Ns + "Arguments") ?? "") != snapshot.DelayArguments)
            throw new StartupException(Problem.Changed);
    }

    private static EntryId DelayId(Snapshot snapshot)
    {
        if (snapshot.Entry.Id.Scope != StartupScope.User || snapshot.DelayTask is not { } path ||
            !path.StartsWith(DelayPrefix, StringComparison.Ordinal) || !Guid.TryParseExact(path[DelayPrefix.Length..], "N", out _) ||
            snapshot.DelaySeconds is < 5 or > 600 || !CommandPath.IsLocalPath(snapshot.DelayFile))
            throw new StartupException(Problem.InvalidStore);
        return new EntryId(StartupKind.Task, StartupScope.Machine, "", path);
    }

    private static ComObject? Find(EntryId id)
    {
        var (directory, name) = Parts(id);
        using var scheduler = Connect();
        try
        {
            using var folder = new ComObject(scheduler.Api.GetFolder(directory));
            return new ComObject(folder.Api.GetTask(name));
        }
        catch (Exception error) when (error.HResult is NotFound or PathNotFound) { return null; }
    }

    private static (string Folder, string Name) Parts(EntryId id)
    {
        int index = id.Name.LastIndexOf('\\');
        if (id.Kind != StartupKind.Task || id.Scope != StartupScope.Machine || id.Site.Length != 0 ||
            !id.Name.StartsWith('\\') || index == id.Name.Length - 1 || id.Name.Contains('\0') ||
            id.Name.Split('\\').Skip(1).Any(segment => segment is "" or "." or ".."))
            throw new StartupException(Problem.BadTarget);
        return (index == 0 ? @"\" : id.Name[..index], id.Name[(index + 1)..]);
    }

    private static Details Require(ComObject task, StartupEntry expected)
    {
        var details = Describe(task);
        if (details.Entry.Actions == EntryActions.None)
            throw new StartupException(Problem.ReadOnly);
        if (!string.Equals(details.Entry.Id.Key, expected.Id.Key, StringComparison.OrdinalIgnoreCase) ||
            details.Entry.Revision != expected.Revision)
            throw new StartupException(Problem.Changed);
        return details;
    }

    private static Details Describe(ComObject task)
    {
        string path = task.Api.Path;
        string name = task.Api.Name;
        bool enabled = task.Api.Enabled;
        string xml = task.Api.Xml;
        XElement root = XElement.Parse(xml);
        var actions = root.Element(Ns + "Actions")?.Elements().Select(action =>
        {
            if (action.Name.LocalName != "Exec")
                return action.Name.LocalName + " " + (string?)action.Element(Ns + "ClassId");
            string command = (string?)action.Element(Ns + "Command") ?? "";
            string arguments = (string?)action.Element(Ns + "Arguments") ?? "";
            return $"\"{command}\" {arguments}".TrimEnd();
        }).ToArray() ?? [];
        var principal = root.Element(Ns + "Principals")?.Element(Ns + "Principal");
        bool system = Protected(path);
        var capability = system ? EntryActions.None : EntryActions.Disable | EntryActions.Enable;
        if (!system && CanRecreate(Logon(principal)))
            capability |= EntryActions.Remove;
        string extra = string.Join(", ", root.Element(Ns + "Triggers")?.Elements().Select(trigger => trigger.Name.LocalName) ?? []);
        root.Element(Ns + "Settings")?.Element(Ns + "Enabled")?.Remove();
        string definition = Hash(root.ToString(SaveOptions.DisableFormatting));
        var entry = new StartupEntry(new EntryId(StartupKind.Task, StartupScope.Machine, "", path), name, path,
            string.Join(Environment.NewLine, actions), actions.Length > 0 ? CommandPath.Target(actions[0]) : null,
            enabled ? EntryState.Active : EntryState.WindowsDisabled, capability, definition + (enabled ? ":1" : ":0"), extra, system);
        return new Details(entry, definition, xml);
    }

    private static int Logon(XElement? principal)
    {
        string? logon = (string?)principal?.Element(Ns + "LogonType");
        string? user = (string?)principal?.Element(Ns + "UserId");
        if (user is "S-1-5-18" or "S-1-5-19" or "S-1-5-20")
            return 5;
        return logon switch
        {
            "S4U" => 2,
            "InteractiveToken" => 3,
            "Group" => 4,
            "ServiceAccount" => 5,
            _ when principal?.Element(Ns + "GroupId") is not null => 4,
            _ => 0
        };
    }

    private static bool CanRecreate(int logon) => logon is 2 or 3 or 4 or 5;

    private static bool Protected(string path) =>
        path.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(DelayPrefix, StringComparison.OrdinalIgnoreCase) ||
        path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static ComObject Connect()
    {
        var service = ComObject.Create("Schedule.Service");
        try { service.Api.Connect(); return service; }
        catch { service.Dispose(); throw; }
    }

    private sealed record Details(StartupEntry Entry, string Definition, string Xml);
}

internal sealed class ComObject(object value) : IDisposable
{
    internal dynamic Api => value;

    internal static ComObject Create(string progId)
    {
        var type = Type.GetTypeFromProgID(progId, throwOnError: true)!;
        return new ComObject(Activator.CreateInstance(type)!);
    }

    public void Dispose() => Marshal.ReleaseComObject(value);
}
