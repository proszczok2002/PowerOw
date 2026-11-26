using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using PowerOw;
using Appearance = PowerOw.Appearance;
using ActivityKind = PowerOw.ActivityKind;

internal static partial class Checks
{
    private static int assertions;

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            if (args.Contains("--diagnose-setup"))
            {
                RequireGuest();
                Maintenance.Run("--setup-install");
                Console.WriteLine("Setup maintenance passed.");
                return 0;
            }
            Translations();
            CatalogAndInterface();
            CheckUpdateBehavior();
            if (args.Contains("--guest"))
                Fixture();
            Console.WriteLine($"{assertions} checks passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Translations()
    {
        foreach (var row in TextCatalog.All)
        {
            Require(row.Value.Length == 8, row.Key);
            string[] originals = Variables(row.Value[0]);
            foreach (string value in row.Value)
            {
                Require(!string.IsNullOrWhiteSpace(value) && !value.Contains('\uFFFD'), row.Key + " contains damaged text");
                Require(originals.SequenceEqual(Variables(value)), row.Key + " has mismatched placeholders");
            }
        }
        Require(TextCatalog.FromWindows(CultureInfo.GetCultureInfo("pl-PL")) == "pl", "Polish Windows fallback");
        Require(TextCatalog.FromWindows(CultureInfo.GetCultureInfo("zh-SG")) == "zh-CN", "Simplified Chinese fallback");
        Require(TextCatalog.FromWindows(CultureInfo.GetCultureInfo("zh-TW")) == "en", "Traditional Chinese fallback");
        Require(TextCatalog.FromWindows(CultureInfo.GetCultureInfo("it-IT")) == "en", "Unsupported language fallback");
        Console.WriteLine($"Catalogs: {TextCatalog.All.Count()} keys in 8 languages, including plural-free format placeholders.");

        static string[] Variables(string value) => Regex.Matches(value, @"\{\d+\}").Select(match => match.Value).OrderBy(x => x).ToArray();
    }

    private static void CatalogAndInterface()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PowerOwAudit-" + Guid.NewGuid().ToString("N"));
        var store = new StateStore(directory);
        var engine = new StartupEngine(StartupScope.User, store);
        try
        {
            var inventory = engine.Load();
            Require(inventory.StateProblem is null, "State directory initialized");
            Require(inventory.Items.Count > 0, "Windows source providers loaded real entries");
            Require(inventory.Items.Any(row => row.Entry.Id.Kind == StartupKind.Service), "Service enumeration");
            Require(inventory.Items.Any(row => row.Entry.Id.Kind == StartupKind.System), "System source enumeration");
            Require(inventory.Items.Any(row => row.Entry.Id.Kind == StartupKind.Task), "Task Scheduler enumeration");
            Require(inventory.Items.All(row => row.Entry.Id.Scope != StartupScope.Machine || !row.CanWrite),
                "User engine cannot perform machine writes");
            var programs = new StartupEngine(StartupScope.User, store, applicationsOnly: true).Load();
            Require(programs.Items.All(row => !row.Entry.WindowsComponent &&
                row.Entry.Id.Kind is not (StartupKind.Driver or StartupKind.System or StartupKind.Wmi)), "Application view excludes system components");
            Console.WriteLine($"Live inventory: {inventory.Items.Count} rows. Unavailable categories: {string.Join(", ", inventory.Unavailable)}.");
            Console.WriteLine($"Application view: {programs.Items.Count} eligible entries across all program categories.");
            foreach (string language in TextCatalog.Codes)
            {
                var text = new TextCatalog(language);
                foreach (ThemeMode theme in Enum.GetValues<ThemeMode>())
                {
                    using var form = new MainForm(engine, new Preferences(language, theme));
                    Appearance.Apply(form, theme);
                    var children = Children(form).ToArray();
                    var list = children.OfType<ListBox>().Single();
                    Require(list.AccessibilityObject.Name == text["Entries"], $"{language}/{theme}: list name");
                    Require(list.SelectionMode == SelectionMode.MultiExtended, $"{language}/{theme}: multi-selection");
                    Require(children.OfType<TextBox>().Any(box => box.ReadOnly && box.AccessibilityObject.Name == text["Details"]),
                        $"{language}/{theme}: readable details");
                    Require(children.OfType<ComboBox>().Count(box => box.AccessibilityObject.Name?.Length > 0) == 2,
                        $"{language}/{theme}: filter names");
                    Require(children.OfType<Button>().All(button => !string.IsNullOrWhiteSpace(button.AccessibilityObject.Name)),
                        $"{language}/{theme}: all button names");
                    if (theme == ThemeMode.Dark && !SystemInformation.HighContrast)
                        Require(form.BackColor == Appearance.Navy && Contrast(list.ForeColor, list.BackColor) > 7, "Dark palette contrast");
                }
            }
            Console.WriteLine("UI structure checked for all 24 language and theme combinations.");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }

        static IEnumerable<Control> Children(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var nested in Children(child))
                    yield return nested;
            }
        }

        static double Contrast(Color first, Color second)
        {
            double Luminance(Color color)
            {
                static double Channel(byte b) => b / 255d <= 0.04045 ? b / 255d / 12.92 : Math.Pow((b / 255d + 0.055) / 1.055, 2.4);
                return Channel(color.R) * 0.2126 + Channel(color.G) * 0.7152 + Channel(color.B) * 0.0722;
            }
            var values = new[] { Luminance(first), Luminance(second) };
            return (values.Max() + 0.05) / (values.Min() + 0.05);
        }
    }

    private static void Fixture()
    {
        RequireGuest();
        using var identity = WindowsIdentity.GetCurrent();
        bool elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        ActiveRegistry(elevated ? StartupScope.Machine : StartupScope.User);
        FolderEntry(elevated ? StartupScope.Machine : StartupScope.User);
        if (elevated)
        {
            Require(Maintenance.GuardRunning(), "Real machine protection service is running");
            Scheduled();
            Service();
        }
        else
        {
            Delay();
        }
    }

    private static void RequireGuest()
    {
        if (!Environment.UserName.Equals("PowerOwAudit", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(@"C:\PowerOwAuditFinal\fixture-session"))
            throw new InvalidOperationException("Run the live mutation suite only in the PowerOw audit VM.");
    }

    private static void ActiveRegistry(StartupScope scope)
    {
        string site = scope == StartupScope.User ? "UserRunOnce" : "MachineRun32";
        var entrySite = RegistryEntries.Sites.Single(item => item.Code == site);
        using var root = RegistryKey.OpenBaseKey(entrySite.Hive, entrySite.View);
        using var key = root.CreateSubKey(entrySite.Path);
        var store = new StateStore(StateStore.Location(scope));
        var engine = new StartupEngine(scope, store);
        var id = new EntryId(StartupKind.Registry, scope, site, "PowerOw Audit " + Guid.NewGuid().ToString("N"));
        string raw = "\"%SystemRoot%\\System32\\cmd.exe\" /d /q /c exit 0";
        try
        {
            key.SetValue(id.Name, raw, RegistryValueKind.ExpandString);
            var original = RegistryEntries.Read(id, false)!;
            var initial = new ViewEntry(original, null, true, true);
            key.SetValue(id.Name, raw + " ", RegistryValueKind.ExpandString);
            Throws(Problem.Changed, () => engine.Apply(initial, Change.RemoveAndGuard));
            key.SetValue(id.Name, raw, RegistryValueKind.ExpandString);
            engine.Apply(initial, Change.DisableAndGuard);
            Require(key.GetValue(id.Name) is null, "Disabled registry value removed");
            engine.SetPaused(true);
            key.SetValue(id.Name, raw + " ", RegistryValueKind.ExpandString);
            engine.GuardTick();
            Require(key.GetValue(id.Name) is not null, "Pause preserves a recurring entry");
            engine.SetPaused(false);
            if (scope == StartupScope.User)
                engine.GuardTick();
            Require(SpinWait.SpinUntil(() => key.GetValue(id.Name) is null, TimeSpan.FromSeconds(12)), "Guard blocked changed command at same name");
            var saved = Find(store, id);
            Require(saved.Mode == SavedMode.Disabled && saved.Guard, "Rule persisted");
            Require(store.Locked(state => state.History.Any(item => item.Kind == ActivityKind.Blocked && item.Entry == id.Name)), "Guard action in history");
            engine.Apply(new ViewEntry(original, saved, false, true), Change.StopGuarding);
            key.SetValue(id.Name, raw + " different", RegistryValueKind.ExpandString);
            var other = RegistryEntries.Read(id, false)!;
            Throws(Problem.Occupied, () => engine.Apply(new ViewEntry(other, Find(store, id), true, true), Change.Restore));
            key.DeleteValue(id.Name);
            engine.Apply(new ViewEntry(original, Find(store, id), false, true), Change.Restore);
            Require(key.GetValueKind(id.Name) == RegistryValueKind.ExpandString &&
                Equals(key.GetValue(id.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), raw), "Registry type and raw text restored exactly");
            Console.WriteLine($"{scope} {site}: disable, pause, recurrence, stop rule, collision and exact restore.");
        }
        finally { engine.SetPaused(false); key.DeleteValue(id.Name, false); }
    }

    private static void FolderEntry(StartupScope scope)
    {
        string directory = Environment.GetFolderPath(scope == StartupScope.User ? Environment.SpecialFolder.Startup : Environment.SpecialFolder.CommonStartup);
        var id = new EntryId(StartupKind.StartupFolder, scope, "", "PowerOw Audit " + Guid.NewGuid().ToString("N") + ".lnk");
        string path = Path.Combine(directory, id.Name);
        // A real .lnk is made without launching the file or changing its target.
        using var shell = ComObject.Create("WScript.Shell");
        using (var link = new ComObject(shell.Api.CreateShortcut(path)))
        {
            link.Api.TargetPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            link.Api.Arguments = "/d /q /c exit 0";
            link.Api.Save();
        }
        byte[] original = File.ReadAllBytes(path);
        var store = new StateStore(StateStore.Location(scope));
        var engine = new StartupEngine(scope, store);
        try
        {
            var current = StartupFolders.Read(id, userApproval: scope == StartupScope.User, describe: true)!;
            Require(current.Target?.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase) == true, "Shortcut target read correctly");
            engine.Apply(new ViewEntry(current, null, true, true), Change.RemoveAndGuard);
            Require(!File.Exists(path), "Shortcut removed");
            File.WriteAllBytes(path, original);
            if (scope == StartupScope.User)
                engine.GuardTick();
            Require(SpinWait.SpinUntil(() => !File.Exists(path), TimeSpan.FromSeconds(12)), "Shortcut recurrence removed");
            engine.Apply(new ViewEntry(current, Find(store, id), false, true), Change.Restore);
            Require(original.SequenceEqual(File.ReadAllBytes(path)), "Shortcut bytes restored exactly");
            Console.WriteLine($"{scope} startup folder: shortcut target, remove, recurrence and byte-for-byte restore.");
        }
        finally { File.Delete(path); }
    }

    private static void Scheduled()
    {
        string name = "PowerOw Audit " + Guid.NewGuid().ToString("N");
        string target = Environment.SystemDirectory + @"\cmd.exe /d /q /c exit 0";
        var id = new EntryId(StartupKind.Task, StartupScope.Machine, "", @"\" + name);
        var store = new StateStore(StateStore.Location(StartupScope.Machine));
        var engine = new StartupEngine(StartupScope.Machine, store);
        try
        {
            Command("schtasks.exe", "/Create", "/TN", name, "/SC", "ONLOGON", "/TR", target,
                "/RU", "SYSTEM", "/F");
            StartupEntry current = Tasks.Read(id)!;
            Require(current.Active && current.Actions.HasFlag(EntryActions.Remove), "Task can be removed with recoverable service account");
            engine.Apply(new ViewEntry(current, null, true, true), Change.DisableAndGuard);
            Require(Tasks.Read(id)?.Active == false, "Task disabled");
            Command("schtasks.exe", "/Change", "/TN", name, "/ENABLE");
            Require(SpinWait.SpinUntil(() => Tasks.Read(id)?.Active == false, TimeSpan.FromSeconds(12)), "Service disables recurring task");
            var disabled = Tasks.Read(id)!;
            engine.Apply(new ViewEntry(disabled, Find(store, id), true, true), Change.Remove);
            Require(Tasks.Read(id) is null, "Task removed");
            engine.Apply(new ViewEntry(current, Find(store, id), false, true), Change.Restore);
            Require(Tasks.Read(id)?.Active == true, "Task restored in the original enabled state");
            Console.WriteLine("Task Scheduler: disable, guard, remove and restore a SYSTEM logon task.");
        }
        finally { Command("schtasks.exe", "/Delete", "/TN", name, "/F"); }
    }

    private static void Service()
    {
        string name = "PowerOwAudit_" + Guid.NewGuid().ToString("N");
        var id = new EntryId(StartupKind.Service, StartupScope.Machine, "", name);
        var store = new StateStore(StateStore.Location(StartupScope.Machine));
        var engine = new StartupEngine(StartupScope.Machine, store);
        try
        {
            Command("sc.exe", "create", name, "binPath=", @"C:\PowerOwAuditFinal\unused-fixture.exe", "start=", "delayed-auto");
            StartupEntry current = Services.Read(id)!;
            engine.Apply(new ViewEntry(current, null, true, true), Change.DisableAndGuard);
            Require(Services.Read(id)?.Active == false, "Service disabled through SCM");
            Command("sc.exe", "config", name, "start=", "auto");
            Require(SpinWait.SpinUntil(() => Services.Read(id)?.Active == false, TimeSpan.FromSeconds(12)), "SCM guard reverted autostart");
            engine.Apply(new ViewEntry(Services.Read(id)!, Find(store, id), true, true), Change.Restore);
            using var key = RegistryEntries.Open(RegistryHive.LocalMachine, RegistryView.Registry64, Services.RegistryPath + "\\" + name);
            Require(key?.GetValue("Start") is 2 && key.GetValue("DelayedAutoStart") is 1, "Delayed automatic service mode restored");
            Console.WriteLine("SCM: disable, guard and restore delayed automatic start.");
        }
        finally { Command("sc.exe", "delete", name); }
    }

    private static void Delay()
    {
        var entrySite = RegistryEntries.Sites.Single(item => item.Code == "UserRun");
        using var root = RegistryKey.OpenBaseKey(entrySite.Hive, entrySite.View);
        using var key = root.CreateSubKey(entrySite.Path);
        var store = new StateStore(StateStore.Location(StartupScope.User));
        var engine = new StartupEngine(StartupScope.User, store);
        var id = new EntryId(StartupKind.Registry, StartupScope.User, entrySite.Code, "PowerOw Delay " + Guid.NewGuid().ToString("N"));
        try
        {
            string command = $"\"{Path.Combine(Environment.SystemDirectory, "cmd.exe")}\" /d /q /c exit 0";
            key.SetValue(id.Name, command);
            StartupEntry current = RegistryEntries.Read(id, true)!;
            engine.Apply(new ViewEntry(current, null, true, true), Change.Delay, 15);
            var copy = Find(store, id);
            var task = new EntryId(StartupKind.Task, StartupScope.Machine, "", copy.Original.DelayTask!);
            Require(Tasks.Read(task)?.Active == true && key.GetValue(id.Name) is null, "Delayed task registered and originally quick Run cleared");
            engine.Apply(new ViewEntry(current, copy, false, true), Change.Restore);
            Require(Tasks.Read(task) is null && Equals(key.GetValue(id.Name), command), "Undo delay removed task and restored Run");
            Console.WriteLine("Standard-user delay registration and undo.");
        }
        finally { key.DeleteValue(id.Name, false); }
    }

    private static SavedEntry Find(StateStore store, EntryId id) => store.Locked(state => state.Saved.Single(item => item.Original.Entry.Id == id));

    private static void Command(string file, params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, file))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string arg in arguments)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        string evidence = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Require(process.ExitCode == 0, file + " failed: " + evidence);
    }

    private static void Throws(Problem problem, Action action)
    {
        try { action(); }
        catch (StartupException error) when (error.Problem == problem) { assertions++; return; }
        throw new InvalidOperationException("Expected " + problem);
    }

    private static void Require(bool passed, string name)
    {
        if (!passed)
            throw new InvalidOperationException(name);
        assertions++;
    }
}
