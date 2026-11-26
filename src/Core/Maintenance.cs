using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;

namespace PowerOw;

internal static class Maintenance
{
    private const string Image = "PowerOw.exe";
    private static string ServiceCommand => $"\"{Path.Combine(InstallPaths.Root, Image)}\" --service";

    internal static void Run(string mode, bool? logon = null, bool? protection = null)
    {
        SafePaths.RequirePlain(InstallPaths.Root);
        if (mode == "--setup-install")
        {
            SecureDataDirectory();
            if (protection is bool guard)
                new StartupEngine(StartupScope.Machine, new StateStore(StateStore.Location(StartupScope.Machine))).SetPaused(!guard);
            if (logon is bool atSignIn)
            {
                Tasks.EnsureLogonTask();
                Tasks.SetLogonEnabled(atSignIn);
            }
            RemoveLegacyRunEntry();
            Install();
            return;
        }
        Stop();
        CloseWindows();
        if (mode == "--setup-remove")
        {
            Tasks.RemoveLogonTask();
            RemoveLegacyRunEntry();
            Remove();
        }
    }

    private static void RemoveLegacyRunEntry()
    {
        using var key = RegistryEntries.Open(RegistryHive.LocalMachine, RegistryView.Registry64, RegistryEntries.Current + "Run", write: true);
        string own = $"\"{Path.Combine(InstallPaths.Root, Image)}\" --tray";
        if (key?.GetValue("PowerOw", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string registered &&
            registered.Equals(own, StringComparison.OrdinalIgnoreCase))
            key.DeleteValue("PowerOw", throwOnMissingValue: false);
    }

    internal static bool GuardRunning()
    {
        try
        {
            using var service = new ServiceController(InstallPaths.ServiceName);
            return service.Status == ServiceControllerStatus.Running && OwnRegistration();
        }
        catch (Exception error) when (Faults.Expected(error) || error is InvalidOperationException) { return false; }
    }

    private static void Install()
    {
        var exists = OwnRegistration();
        if (!exists)
        {
            using var manager = Services.OpenSCManager(null, null, 2);
            if (manager.IsInvalid)
                throw LastError();
            using var created = CreateService(manager, InstallPaths.ServiceName, "PowerOw", 0x10,
                0x10, 2, 1, ServiceCommand, null, IntPtr.Zero, null, null, null);
            if (created.IsInvalid)
                throw LastError();
        }
        try
        {
            using var service = new ServiceController(InstallPaths.ServiceName);
            if (service.Status != ServiceControllerStatus.Running)
            {
                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
            }
        }
        catch
        {
            if (!exists)
            {
                Stop();
                Remove();
            }
            throw;
        }
    }

    private static void Stop()
    {
        if (!OwnRegistration())
            return;
        using var service = new ServiceController(InstallPaths.ServiceName);
        if (service.Status == ServiceControllerStatus.Stopped)
            return;
        if (service.Status == ServiceControllerStatus.StartPending)
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
        if (service.Status != ServiceControllerStatus.StopPending)
            service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
    }

    private static void Remove()
    {
        if (!OwnRegistration())
            return;
        using var manager = Services.OpenSCManager(null, null, 1);
        if (manager.IsInvalid)
            throw LastError();
        using var service = Services.OpenService(manager, InstallPaths.ServiceName, 0x10000);
        if (service.IsInvalid)
            throw LastError();
        if (!DeleteService(service) && Marshal.GetLastWin32Error() != 1072)
            throw LastError();
    }

    private static bool OwnRegistration()
    {
        using var key = RegistryEntries.Open(RegistryHive.LocalMachine, RegistryView.Registry64,
            $@"{Services.RegistryPath}\{InstallPaths.ServiceName}");
        if (key is null)
            return false;
        string? registration = key.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        if (!string.Equals(registration, ServiceCommand, StringComparison.OrdinalIgnoreCase))
            throw new StartupException(Problem.Occupied);
        return true;
    }

    private static void SecureDataDirectory()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var role in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
        {
            var sid = new SecurityIdentifier(role, null);
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }
        string directory = StateStore.Location(StartupScope.Machine);
        var info = new DirectoryInfo(directory);
        if (!info.Exists)
            info.Create(security);
        else
        {
            SafePaths.RequirePlain(directory);
            info.SetAccessControl(security);
        }
    }

    private static void CloseWindows()
    {
        foreach (var process in Process.GetProcessesByName("PowerOw"))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId)
                    continue;
                try
                {
                    if (!string.Equals(process.MainModule?.FileName, Path.Combine(InstallPaths.Root, Image), StringComparison.OrdinalIgnoreCase))
                        continue;
                    process.Kill();
                    if (!process.WaitForExit(8000))
                        throw new System.TimeoutException();
                }
                catch (InvalidOperationException) when (process.HasExited) { }
                catch (Win32Exception) when (process.HasExited) { }
            }
        }
    }

    private static Win32Exception LastError() => new(Marshal.GetLastWin32Error());

    [DllImport("advapi32.dll", EntryPoint = "CreateServiceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Services.ServiceHandle CreateService(Services.ServiceHandle manager, string name, string display,
        uint desired, uint type, uint start, uint error, string image, string? group, IntPtr tag, string? dependencies,
        string? account, string? password);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeleteService(Services.ServiceHandle service);
}

internal sealed class GuardianService : ServiceBase
{
    private CancellationTokenSource? cancellation;
    private Task? loop;

    internal GuardianService()
    {
        ServiceName = InstallPaths.ServiceName;
        CanStop = true;
        CanShutdown = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args)
    {
        SafePaths.RequirePlain(StateStore.Location(StartupScope.Machine));
        cancellation = new CancellationTokenSource();
        CancellationToken token = cancellation.Token;
        var engine = new StartupEngine(StartupScope.Machine, new StateStore(StateStore.Location(StartupScope.Machine)));
        loop = Task.Run(async () =>
        {
            using var clock = new PeriodicTimer(TimeSpan.FromSeconds(5));
            try
            {
                do
                {
                    try { engine.GuardTick(); }
                    catch (Exception error) when (Faults.Expected(error)) { }
                }
                while (await clock.WaitForNextTickAsync(token));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }, token);
    }

    protected override void OnStop()
    {
        cancellation?.Cancel();
        try { _ = loop?.Wait(TimeSpan.FromSeconds(8)); }
        catch (AggregateException error) when (error.InnerExceptions.All(inner => inner is OperationCanceledException)) { }
        cancellation?.Dispose();
        cancellation = null;
    }

    protected override void OnShutdown() => OnStop();
}
