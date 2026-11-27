if ($Stage -ne 'StartupSources' -or $env:USERNAME -ne 'PowerOwAudit' -or !(Test-Path -LiteralPath "$root\fixture-session")) {
    throw 'Use GuestUiAudit.ps1 -Stage StartupSources in the disposable VM.'
}

$tag = [Guid]::NewGuid().ToString('N').Substring(0, 5)
$packageName = 'Proszczok.PowerOw.NotificationProbe'
$packaged = 'PowerOw QA MSIX Desktop','PowerOw QA MSIX UWP'
$msix = Join-Path $root 'StartupProbe.msix'
$publicKey = Join-Path $root 'StartupProbe.cer'
if (!(Test-Path -LiteralPath $msix) -or !(Test-Path -LiteralPath $publicKey)) { throw 'The signed MSIX fixture is missing.' }
$certificate = $null
$registrySources = foreach ($site in 'CurrentUser|Registry64|U','LocalMachine|Registry64|M64','LocalMachine|Registry32|M32') {
    $hive, $view, $label = $site.Split('|')
    foreach ($suffix in 'Run','RunOnce','Policies\Explorer\Run') {
        [pscustomobject]@{
            Hive = [Microsoft.Win32.RegistryHive]$hive
            View = [Microsoft.Win32.RegistryView]$view
            Path = 'Software\Microsoft\Windows\CurrentVersion\' + $suffix
            Name = "PowerOw $label $($suffix.Replace('Policies\Explorer\Run','Policy')) $tag"
        }
    }
}
$files = @(
    (Join-Path ([Environment]::GetFolderPath('Startup')) "PowerOw U cmd $tag.cmd")
    (Join-Path ([Environment]::GetFolderPath('Startup')) "PowerOw U lnk $tag.lnk")
    (Join-Path ([Environment]::GetFolderPath('CommonStartup')) "PowerOw M cmd $tag.cmd")
    (Join-Path ([Environment]::GetFolderPath('CommonStartup')) "PowerOw M lnk $tag.lnk")
)
$logonTask = "PowerOw logowanie $tag"
$bootTask = "PowerOw start $tag"
$inactiveTask = "PowerOw wyłączone $tag"
$dailyTask = "PowerOw okresowe $tag"
$autoService = "PowerOwQA-Auto-$tag"
$delayService = "PowerOwQA-Delay-$tag"
$manualService = "PowerOwQA-Manual-$tag"
$systemService = "PowerOwQA-System-$tag"
$serviceProbe = Join-Path $root "service-$tag.exe"
$statePath = Join-Path $env:LOCALAPPDATA 'PowerOw\startup.json'
$since = [DateTimeOffset]::Now
$action = New-ScheduledTaskAction -Execute $env:ComSpec -Argument '/d /q /c exit 0'
$expected = [Collections.Generic.List[string]]::new()

function Test-Batch([string]$label, [string[]]$names, [scriptblock]$create) {
    $skip = @(Get-Content -LiteralPath $speech -Encoding UTF8).Count
    & $create
    foreach ($name in $names) {
        Expect-NewSpeech $skip ([regex]::Escape($name)) 37
        $expected.Add($name)
    }
    if ([AuditInput]::GetForegroundWindow() -ne $foreground) { throw "Notification for $label moved focus." }
    if (Window-With '^PowerOw v[0-9]') { throw "Notification for $label opened the main window." }
    Write-Result "Passed $label for $($names.Count) entries in the background."
}

try {
    Restart-Tray
    $foreground = Open-ExplorerInForeground
    Start-Sleep -Seconds 12
    $speechStart = @(Get-Content -LiteralPath $speech -Encoding UTF8).Count
    foreach ($group in ($registrySources | Group-Object Hive,View)) {
        Test-Batch $group.Name $group.Group.Name {
            foreach ($source in $group.Group) {
                $hiveKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey($source.Hive,$source.View)
                try {
                    $subkey = $hiveKey.CreateSubKey($source.Path)
                    try { $subkey.SetValue($source.Name, $command, [Microsoft.Win32.RegistryValueKind]::ExpandString) }
                    finally { $subkey.Dispose() }
                }
                finally { $hiveKey.Dispose() }
            }
        }
    }
    Test-Batch 'both Startup folders, files and shortcuts' @($files | ForEach-Object { Split-Path -Leaf $_ }) {
        foreach ($file in $files) {
            if ($file.EndsWith('.cmd')) {
                Set-Content -LiteralPath $file -Value '@exit /b 0' -Encoding ASCII
            }
            else {
                $shortcut = $shell.CreateShortcut($file)
                $shortcut.TargetPath = $env:ComSpec
                $shortcut.Arguments = '/d /q /c exit 0'
                $shortcut.Save()
            }
        }
    }
    Test-Batch 'logon and boot tasks, including a disabled task' @($logonTask,$bootTask,$inactiveTask) {
        $logon = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
        $boot = New-ScheduledTaskTrigger -AtStartup
        $disabled = New-ScheduledTaskSettingsSet -Disable
        $daily = New-ScheduledTaskTrigger -Daily -At '23:59'
        Register-ScheduledTask -TaskName $logonTask -Action $action -Trigger $logon -Force | Out-Null
        Register-ScheduledTask -TaskName $bootTask -Action $action -Trigger $boot -Force | Out-Null
        Register-ScheduledTask -TaskName $inactiveTask -Action $action -Trigger $boot -Settings $disabled -Force | Out-Null
        Register-ScheduledTask -TaskName $dailyTask -Action $action -Trigger $daily -Force | Out-Null
    }
    Test-Batch 'automatic and delayed application services' @($autoService,$delayService) {
        Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\whoami.exe') -Destination $serviceProbe
        New-Service -Name $autoService -BinaryPathName $serviceProbe -StartupType Automatic | Out-Null
        & sc.exe create $delayService binPath= $serviceProbe start= delayed-auto | Out-Null
        if ($LASTEXITCODE) { throw 'Delayed-start service could not be registered.' }
        New-Service -Name $manualService -BinaryPathName $serviceProbe -StartupType Manual | Out-Null
        New-Service -Name $systemService -BinaryPathName (Join-Path $env:SystemRoot 'System32\whoami.exe') -StartupType Automatic | Out-Null
    }
    $certificate = Import-Certificate -FilePath $publicKey -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'
    Test-Batch 'installed MSIX with desktop and UWP StartupTask declarations' $packaged {
        Add-AppxPackage -Path $msix
        if (!(Get-AppxPackage -Name $packageName)) { throw 'The MSIX fixture was not installed.' }
    }
    Start-Sleep -Seconds 22
    $excluded = $dailyTask,$manualService,$systemService
    $spoken = @(Get-Content -LiteralPath $speech -Encoding UTF8 | Select-Object -Skip $speechStart | Where-Object { $_ -match 'Speaking \[' })
    $events = (Get-Content -LiteralPath $statePath -Encoding UTF8 -Raw | ConvertFrom-Json).History |
        Where-Object { $_.Kind -eq 7 -and [DateTimeOffset]$_.When -ge $since }
    foreach ($name in $expected) {
        $records = @($events | Where-Object Entry -eq $name)
        if ($records.Count -ne 1 -or [string]::IsNullOrWhiteSpace($records[0].Location)) {
            throw "Missing or duplicate activity record for $name."
        }
        if (@($spoken -match ([regex]::Escape($name))).Count -ne 1) { throw "Duplicate speech for $name." }
        Write-Result "$name at $($records[0].Location)"
    }
    foreach ($name in $excluded) {
        if (($events.Entry -contains $name) -or ($spoken -match ([regex]::Escape($name)))) { throw "Unexpected notification for $name." }
    }
    Write-Result "Saved $($expected.Count) distinct entries with locations. Unchanged entries, the timed task and excluded services stayed silent."
    Verify-StartupHistory $expected
    Write-Result "All entries were readable in the activity log. Defender real-time protection=$((Get-MpComputerStatus).RealTimeProtectionEnabled)."
}
finally {
    foreach ($source in $registrySources) {
        $hiveKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey($source.Hive,$source.View)
        try {
            $subkey = $hiveKey.OpenSubKey($source.Path, $true)
            if ($subkey) {
                try { $subkey.DeleteValue($source.Name, $false) }
                finally { $subkey.Dispose() }
            }
        }
        finally { $hiveKey.Dispose() }
    }
    Remove-Item -LiteralPath $files -ErrorAction SilentlyContinue
    foreach ($name in $logonTask,$bootTask,$inactiveTask,$dailyTask) {
        Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
    }
    foreach ($name in $autoService,$delayService,$manualService,$systemService) {
        if (Get-Service -Name $name -ErrorAction SilentlyContinue) { & sc.exe delete $name | Out-Null }
    }
    Remove-Item -LiteralPath $serviceProbe -ErrorAction SilentlyContinue
    Get-AppxPackage -Name $packageName | Remove-AppxPackage
    if ($certificate) { Remove-Item -LiteralPath (Join-Path 'Cert:\LocalMachine\TrustedPeople' $certificate.Thumbprint) }
}
