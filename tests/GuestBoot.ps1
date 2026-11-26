param([Parameter(Mandatory=$true)][bool]$Expected)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class AuditToken {
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("advapi32.dll")] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll")] private static extern bool GetTokenInformation(IntPtr token, int cls, out int value, int bytes, out int length);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    public static int Elevated(int pid) {
        IntPtr process = OpenProcess(0x1000, false, pid), token;
        if (process == IntPtr.Zero) return -1;
        try {
            if (!OpenProcessToken(process, 8, out token)) return -2;
            try { int value,length; return GetTokenInformation(token,20,out value,4,out length) ? value : -3; }
            finally { CloseHandle(token); }
        } finally { CloseHandle(process); }
    }
}
'@
$running = @(Get-Process PowerOw -ErrorAction SilentlyContinue | Where-Object SessionId -gt 0)
$consent = @(Get-Process consent -ErrorAction SilentlyContinue)
$boot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
"Boot $($boot.ToString('s'))"
"Interactive PowerOw processes: $($running.Count)"
"Consent.exe processes: $($consent.Count)"
if ($Expected -ne ($running.Count -gt 0) -or $consent.Count -ne 0) { throw 'Logon behavior does not match the configured option.' }
foreach ($process in $running) {
    $high = [AuditToken]::Elevated($process.Id)
    "PowerOw PID $($process.Id), session $($process.SessionId), elevated token $high"
    if ($high -ne 1) { throw 'The interactive PowerOw did not have an elevated token.' }
}
(Get-Service PowerOwGuard).Status
