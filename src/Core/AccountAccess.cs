using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace PowerOw;

internal static class AccountAccess
{
    private static readonly Guid LocalData = new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");
    private static readonly Guid Startup = new("B97D20BB-F46A-4C97-BA10-5E3608430854");
    private static SafeAccessTokenHandle? shellToken;
    [ThreadStatic] private static bool impersonating;
    internal static string? Sid { get; private set; }
    internal static bool Available { get; private set; }

    internal static void Initialize()
    {
        // The main window is elevated. Registry and file operations belonging to the desktop account
        // use Explorer's token so that redirected folders and registry links cannot gain write access.
        uint processId = 0;
        IntPtr shell = GetShellWindow();
        if (shell != IntPtr.Zero)
            _ = GetWindowThreadProcessId(shell, out processId);
        if (processId != 0)
        {
            using var process = OpenProcess(0x1000, false, processId);
            if (!process.IsInvalid && OpenProcessToken(process, 0xE, out var token))
                shellToken = token;
        }
        using var identity = WindowsIdentity.GetCurrent();
        if (shellToken is null && GetTokenInformation(identity.AccessToken, 19, out IntPtr linked, IntPtr.Size, out _))
        {
            var candidate = new SafeAccessTokenHandle(linked);
            using var account = new WindowsIdentity(candidate.DangerousGetHandle());
            if (!new WindowsPrincipal(account).IsInRole(WindowsBuiltInRole.Administrator))
                shellToken = candidate;
            else
                candidate.Dispose();
        }
        if (shellToken is not null)
        {
            using var desktopIdentity = new WindowsIdentity(shellToken.DangerousGetHandle());
            Sid = desktopIdentity.User?.Value;
            Available = Sid is not null;
        }
    }

    internal static void Dispose()
    {
        shellToken?.Dispose();
        shellToken = null;
    }

    internal static T Run<T>(Func<T> action)
    {
        if (shellToken is null || impersonating)
            return action();
        return WindowsIdentity.RunImpersonated(shellToken, () =>
        {
            impersonating = true;
            try { return action(); }
            finally { impersonating = false; }
        });
    }

    internal static string Folder(Environment.SpecialFolder folder)
    {
        if (shellToken is null || folder is not (Environment.SpecialFolder.LocalApplicationData or Environment.SpecialFolder.Startup))
            return Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
        Guid id = folder == Environment.SpecialFolder.Startup ? Startup : LocalData;
        int result = SHGetKnownFolderPath(ref id, 0x4000, shellToken, out var name);
        try
        {
            return result >= 0 ? Marshal.PtrToStringUni(name) ?? "" : throw new StartupException(Problem.BadTarget);
        }
        finally { Marshal.FreeCoTaskMem(name); }
    }

    internal static RegistryKey OpenUser(RegistryView view)
    {
        if (!impersonating)
            return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
        int result = RegOpenCurrentUser(0x02000000 | (view == RegistryView.Registry32 ? 0x200 : 0x100), out var handle);
        if (result != 0)
            throw new System.ComponentModel.Win32Exception(result);
        return RegistryKey.FromHandle(handle, view);
    }

    internal static Task<int> StartUpdateConsent(string launcher, string language, bool tray)
    {
        if (shellToken is null || !GetTokenInformation(shellToken, 20, out int elevated, sizeof(int), out _) || elevated != 0)
            throw new StartupException(Problem.AccessDenied);
        if (!DuplicateTokenEx(shellToken, 0x02000000, IntPtr.Zero, 2, 1, out var primary))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx");
        using (primary)
        {
            var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Desktop = @"winsta0\default" };
            var line = new StringBuilder($"\"{launcher}\" {language} {(tray ? "1" : "0")}");
            if (!CreateProcessWithTokenW(primary, 0, launcher, line, 0, IntPtr.Zero,
                    Path.GetDirectoryName(launcher)!, ref startup, out var process))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessWithTokenW");
            _ = CloseHandle(process.Thread);
            return Task.Run(() =>
            {
                using var handle = new SafeProcessHandle(process.Process, ownsHandle: true);
                if (WaitForSingleObject(handle, uint.MaxValue) != 0 || !GetExitCodeProcess(handle, out int status))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetExitCodeProcess");
                return status;
            });
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public int X, Y, Width, Height, Columns, Rows, Fill, Flags;
        public short ShowWindow, ReservedSize;
        public IntPtr Reserved2, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint process);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int kind, out IntPtr value, int size, out int returned);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int kind, out int value, int size, out int returned);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(SafeAccessTokenHandle token, uint access, IntPtr attributes,
        int impersonationLevel, int type, out SafeAccessTokenHandle primary);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool CreateProcessWithTokenW(SafeAccessTokenHandle token, int logonFlags, string application,
        StringBuilder commandLine, int flags, IntPtr environment, string workingDirectory, ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint timeout);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle handle, out int status);
    [DllImport("advapi32.dll")]
    private static extern int RegOpenCurrentUser(int access, out SafeRegistryHandle key);
    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, SafeAccessTokenHandle token, out IntPtr path);
}
