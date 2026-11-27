[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Setup','App','Tray','SettingsOff','SettingsOn','PrepareUninstall','ObserveUninstall','VerifyUninstall',
        'UpdatePaused','UpdateBackground','UpdateRetry','VerifyUpdate','Support','Notifications','StartupSources','VerifyStartupHistory',
        'NotificationsPaused')][string]$Stage,
    [switch]$ExistingProtectionRule,
    [switch]$UseDefault,
    [switch]$AttachedSetup,
    [switch]$AttachedLicense,
    [switch]$ActivateInstallOptions,
    [switch]$LeaveInstallerDefaults,
    [switch]$PauseInstallerProtection,
    [switch]$SkipLaunch,
    [switch]$ExistingUpdateOffer,
    [switch]$MeasureLegacyNotifications,
    [switch]$TaskMissing,
    [ValidateSet('Both','Logon','Protection')][string]$SettingsToChange = 'Both',
    [ValidatePattern('^v\d+\.\d+(\.\d+){0,2}$')][string]$ExpectedUpdateVersion = 'v1.0.1',
    [ValidatePattern('^[0-9a-f]{5}$')][string]$StartupProbeTag
)
$ErrorActionPreference = 'Stop'
$root = 'C:\PowerOwAuditFinal'
if ($env:USERNAME -ne 'PowerOwAudit' -or -not (Test-Path -LiteralPath "$root\fixture-session")) {
    throw 'Run the mutation audit only in the disposable Windows VM.'
}
$record = Join-Path $root ("ui-" + $Stage.ToLowerInvariant() + '.log')
$speech = Join-Path $root ("nvda-" + $Stage.ToLowerInvariant() + '.log')
$default = Join-Path $env:ProgramFiles 'PowerOw'
$custom = if ($UseDefault) { $default } else { Join-Path $env:ProgramFiles 'PowerOw QA' }
$installKey = 'HKLM:\Software\Proszczok\PowerOw'
$program = (Get-ItemProperty -LiteralPath $installKey -ErrorAction SilentlyContinue).Directory
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class AuditInput {
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")]
    public static extern IntPtr GetFocus();
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
    private static uint Extended(byte key) { return (key >= 0x21 && key <= 0x2E) || key == 0x5B || key == 0x5D ? 1u : 0u; }
    public static void Down(byte key) { keybd_event(key, 0, Extended(key), UIntPtr.Zero); }
    public static void Up(byte key) { keybd_event(key, 0, Extended(key) | 2, UIntPtr.Zero); }
}
'@
$shell = New-Object -ComObject WScript.Shell
$elementType = [Windows.Automation.AutomationElement]
$controlType = [Windows.Automation.ControlType]
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$fixture = 'PowerOw Próba dostępności'
$command = '"%SystemRoot%\System32\cmd.exe" /d /q /c exit 0'
$uninstallPath = Join-Path $root 'uninstall-path.txt'
$outside = Join-Path $root 'uninstall-sentinel'
$sentinel = Join-Path $outside 'sentinel.txt'

function Write-Result([string]$line) { Add-Content -LiteralPath $record -Value $line -Encoding UTF8 }
function Wait-For([scriptblock]$condition, [string]$description, [int]$seconds=20) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt $seconds) {
        try { $result = & $condition; if ($result) { return $result } }
        catch [Windows.Automation.ElementNotAvailableException] { }
        Start-Sleep -Milliseconds 120
    }
    throw "Timed out waiting for $description."
}
function Find-In($container, [string]$name) {
    $condition = [Windows.Automation.PropertyCondition]::new($elementType::NameProperty, $name)
    $container.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}
function Get-Window([string]$name) {
    [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children,
        [Windows.Automation.PropertyCondition]::new($elementType::NameProperty, $name))
}
function Window-With([string]$pattern, [int]$pidFilter=0) {
    $walker = [Windows.Automation.TreeWalker]::RawViewWalker
    for ($node = $elementType::FocusedElement; $node; $node = $walker.GetParent($node)) {
        if ($node.Current.ControlType -eq $controlType::Window -and $node.Current.Name -match $pattern -and
            ($pidFilter -eq 0 -or $node.Current.ProcessId -eq $pidFilter) -and !$node.Current.IsOffscreen) { return $node }
    }
    foreach ($window in $elementType::RootElement.FindAll([Windows.Automation.TreeScope]::Children, [Windows.Automation.Condition]::TrueCondition)) {
        if ($window.Current.Name -match $pattern -and ($pidFilter -eq 0 -or $window.Current.ProcessId -eq $pidFilter) -and
            !$window.Current.IsOffscreen) { return $window }
    }
}
function Press([string]$sequence) {
    $navigation = @{ '{LEFT}'=0x25; '{UP}'=0x26; '{RIGHT}'=0x27; '{DOWN}'=0x28; '{END}'=0x23; '{HOME}'=0x24 }
    if ($navigation.ContainsKey($sequence)) { NativeKey @($navigation[$sequence]); return }
    $shell.SendKeys($sequence)
    Start-Sleep -Milliseconds 520
}
function NativeKey([byte[]]$keys) {
    foreach ($key in $keys) { [AuditInput]::Down($key) }
    for ($i=$keys.Length-1; $i -ge 0; $i--) { [AuditInput]::Up($keys[$i]) }
    Start-Sleep -Milliseconds 570
}
function Expect-Focus([string]$pattern) {
    $item = Wait-For { $focused = $elementType::FocusedElement; if ($focused -and $focused.Current.Name -match $pattern) { $focused } } "focus $pattern" 7
    Write-Result ("Focus " + $item.Current.ControlType.ProgrammaticName + " — " + $item.Current.Name)
    return $item
}
function Text-Value($element) {
    if (-not $element) { return '' }
    $pattern = $null
    if ($element.TryGetCurrentPattern([Windows.Automation.ValuePattern]::Pattern,[ref]$pattern)) { return $pattern.Current.Value }
    if ($element.TryGetCurrentPattern([Windows.Automation.TextPattern]::Pattern,[ref]$pattern)) { return $pattern.DocumentRange.GetText(-1) }
    return $element.Current.Name
}
function Wait-Stage([string]$name) {
    $match = Wait-For { Find-In $elementType::RootElement $name } $name 27
    Write-Result ("Visible " + $match.Current.Name)
    return $match
}
function Wait-Speech([string]$phrase, [int]$seconds=18) {
    $null = Wait-For { Select-String -LiteralPath $speech -SimpleMatch $phrase -Quiet } "NVDA speech $phrase" $seconds
    Write-Result ("NVDA " + $phrase)
}
function Current-Speech([string]$expression) {
    $lines = @(Get-Content -LiteralPath $speech -Tail 35 -Encoding UTF8)
    $spoken = @($lines | Where-Object { $_ -match 'Speaking \[' })
    if ($spoken.Count -eq 0) { return $false }
    return $spoken[-1] -match $expression
}
function Expect-NewSpeech([int]$skip, [string]$expression, [int]$seconds=9) {
    $null = Wait-For { @(Get-Content -LiteralPath $speech -Encoding UTF8 | Select-Object -Skip $skip |
        Where-Object { $_ -match 'Speaking \[' -and $_ -match $expression }).Count -gt 0 } "new NVDA speech $expression" $seconds
    Write-Result "NVDA read $expression."
}
function Select-InstallerTask([string]$phrase) {
    for ($i=0; $i -lt 5 -and -not (Current-Speech $phrase); $i++) { Press '{DOWN}' }
    if (-not (Current-Speech $phrase)) { throw "NVDA did not read task $phrase." }
    Press ' '
    Write-Result ("Space toggled " + $phrase)
}
function Tab-To([string]$pattern) {
    for ($i=0; $i -lt 20 -and $elementType::FocusedElement.Current.Name -notmatch $pattern; $i++) { Press '{TAB}' }
    Expect-Focus $pattern
}
function Start-Reader {
    $config = Join-Path $root ("profile-" + $Stage)
    New-Item -ItemType Directory -Path $config -Force | Out-Null
    "[general]`r`n    language = pl`r`n[speech]`r`n    synth = espeak`r`n" | Set-Content -LiteralPath (Join-Path $config 'nvda.ini') -Encoding UTF8
    $args = @('--config-path', $config, '--log-file', $speech, '--debug-logging', '--no-sr-flag', '--minimal', '--disable-addons')
    $process = Start-Process -FilePath (Join-Path $root 'NVDA\nvda_noUIAccess.exe') -ArgumentList $args -PassThru -WindowStyle Hidden
    $null = Wait-For { (Test-Path -LiteralPath $speech) -and (Select-String -LiteralPath $speech -SimpleMatch 'NVDA initialized' -Quiet) } 'NVDA startup' 25
    Write-Result 'NVDA started.'
    return $process
}
function Open-Application {
    Start-Process -FilePath (Join-Path $program 'PowerOw.exe')
    $main = Wait-For { Window-With '^PowerOw v[0-9]' } 'PowerOw window'
    $process = $main.Current.ProcessId
    Start-Sleep -Seconds 3
    if ($elementType::FocusedElement.Current.ProcessId -ne $process) {
        $null = $shell.AppActivate($process)
        Write-Result 'Activated the open PowerOw window before sending keyboard commands.'
    }
    $null = Wait-For { $elementType::FocusedElement.Current.ProcessId -eq $process } 'focus in PowerOw'
}
function Focus-Fixture {
    Press '^f'
    $null = Expect-Focus '^Szukaj wpisów autostartu$'
    Press 'PowerOw '
    Press '{TAB}'
    Press '{END}'
    $null = Expect-Focus 'Próba dostępności'
}
function Accept-Confirmation {
    $null = Expect-Focus '^(No|Nie)$'
    Press '{LEFT}'
    $null = Expect-Focus '^(Yes|Tak)$'
    Press '{ENTER}'
}
function Put-Fixture {
    New-ItemProperty -LiteralPath $registry -Name $fixture -Value $command -PropertyType ExpandString -Force | Out-Null
}
function Has-Fixture { $null -ne (Get-ItemProperty -LiteralPath $registry -Name $fixture -ErrorAction SilentlyContinue) }
function Set-Checkbox($control, [bool]$enabled) {
    $pattern = $control.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    if (($pattern.Current.ToggleState -eq 'On') -ne $enabled) { Press ' ' }
    if (($pattern.Current.ToggleState -eq 'On') -ne $enabled) { throw 'Keyboard did not change checkbox state.' }
    Write-Result "Checkbox $($control.Current.Name) checked=$enabled."
}

function Update-Fixture([string]$Name) {
    $location = 'C:\PowerOwUpdateAudit'
    if (!(Test-Path -LiteralPath (Join-Path $location 'fixture-session'))) { throw 'Update fixture is missing.' }
    Join-Path $location $Name
}
function Restart-Tray {
    Get-Process -Name PowerOw -ErrorAction SilentlyContinue | Where-Object SessionId -gt 0 | Stop-Process -Force
    Start-ScheduledTask -TaskName 'PowerOw'
    $null = Wait-For { Get-Process -Name PowerOw -ErrorAction SilentlyContinue | Where-Object SessionId -gt 0 } 'tray process'
}

function Open-ExplorerInForeground {
    NativeKey @(0x5B,0x45)
    Wait-For {
        $handle = [AuditInput]::GetForegroundWindow()
        $item = $elementType::FromHandle($handle)
        if (Get-Process explorer | Where-Object Id -eq $item.Current.ProcessId) { $handle }
    } 'Explorer in the foreground'
}

function Test-StartupNotifications {
    $existing = 'PowerOw QA istniejący wpis'
    $added = 'PowerOw QA wpis rejestru'
    $logon = 'PowerOw QA zadanie logowania'
    $daily = 'PowerOw QA zadanie okresowe'
    $folder = Join-Path ([Environment]::GetFolderPath('Startup')) 'PowerOw QA folder startowy.cmd'
    $action = New-ScheduledTaskAction -Execute $env:ComSpec -Argument '/d /q /c exit 0'
    $start = @(Get-Content -LiteralPath $speech -Encoding UTF8).Count
    try {
        New-ItemProperty -LiteralPath $registry -Name $existing -Value $command -PropertyType ExpandString -Force | Out-Null
        Restart-Tray
        $foreground = Open-ExplorerInForeground
        Start-Sleep -Seconds 12
        $skip = @(Get-Content -LiteralPath $speech -Encoding UTF8).Count
        New-ItemProperty -LiteralPath $registry -Name $added -Value $command -PropertyType ExpandString -Force | Out-Null
        Register-ScheduledTask -TaskName $daily -Action $action -Trigger (New-ScheduledTaskTrigger -Daily -At '23:59') -Force | Out-Null
        try { Expect-NewSpeech $skip ([regex]::Escape($added)) 35 }
        catch { if ($MeasureLegacyNotifications) { Write-Result "Legacy registry notification: $_" } else { throw } }
        if (!$MeasureLegacyNotifications) {
            if ([AuditInput]::GetForegroundWindow() -ne $foreground) { throw 'Notification moved keyboard focus.' }
            $skip = @(Get-Content -LiteralPath $speech -Encoding UTF8).Count
            Set-Content -LiteralPath $folder -Value '@exit /b 0' -Encoding ASCII
            Expect-NewSpeech $skip 'PowerOw QA folder startowy' 35
        }
        $skip = @(Get-Content -LiteralPath $speech -Encoding UTF8).Count
        Register-ScheduledTask -TaskName $logon -Action $action -Trigger (New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME) -Force | Out-Null
        try { Expect-NewSpeech $skip ([regex]::Escape($logon)) 35 }
        catch { if ($MeasureLegacyNotifications) { Write-Result "Legacy logon task notification: $_" } else { throw } }
        if (Window-With '^PowerOw v[0-9]') { throw 'A notification opened the main PowerOw window.' }
        if ([AuditInput]::GetForegroundWindow() -ne $foreground) { throw 'Notification moved keyboard focus.' }
        $spoken = @(Get-Content -LiteralPath $speech -Encoding UTF8 | Select-Object -Skip $start | Where-Object { $_ -match 'Speaking \[' })
        if (($spoken -match ([regex]::Escape($existing))) -or ($spoken -match ([regex]::Escape($daily)))) {
            throw 'NVDA announced an existing entry or a task unrelated to startup.'
        }
        Write-Result 'Explorer kept focus. No old entries or daily tasks were announced.'
        if (!$MeasureLegacyNotifications) {
            Verify-StartupHistory @($added,$logon,(Split-Path -Leaf $folder))
        }
    }
    finally {
        Remove-ItemProperty -LiteralPath $registry -Name $existing,$added -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $folder -ErrorAction SilentlyContinue
        foreach ($task in $logon,$daily) { Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction SilentlyContinue }
    }
}
function Test-NotificationProtection {
    $statePath = Join-Path $program 'Data\startup.json'
    $journal = Join-Path $env:LOCALAPPDATA 'PowerOw\startup.json'
    $tag = [guid]::NewGuid().ToString('N').Substring(0, 6)
    $whilePaused = "PowerOw wpis bez ochrony $tag"
    $whileActive = "PowerOw wpis z ochroną $tag"
    $original = -not (Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json).Paused

    function Set-Protection([bool]$enabled) {
        Open-Application
        NativeKey @(0x11,0xBC)
        $check = Tab-To '^Pilnuj wpisów autostartu$'
        Set-Checkbox $check $enabled
        Press '{ENTER}'
        $null = Wait-For { -not (Window-With '^Ustawienia PowerOw$') } 'settings closed'
        $null = Wait-For { (Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json).Paused -ne $enabled } 'protection state saved'
        Press '%{F4}'
        $null = Wait-For { -not (Window-With '^PowerOw v[0-9]') } 'main window hidden'
    }

    try {
        Set-Protection $false
        $null = Open-ExplorerInForeground
        $skip = @(Get-Content -LiteralPath $speech -Encoding UTF8).Count
        New-ItemProperty -LiteralPath $registry -Name $whilePaused -Value $command -PropertyType ExpandString -Force | Out-Null
        Start-Sleep -Seconds 23
        Set-Protection $true
        $foreground = Open-ExplorerInForeground
        Start-Sleep -Seconds 8
        $resume = @(Get-Content -LiteralPath $speech -Encoding UTF8).Count
        New-ItemProperty -LiteralPath $registry -Name $whileActive -Value $command -PropertyType ExpandString -Force | Out-Null
        Expect-NewSpeech $resume ([regex]::Escape($whileActive)) 35
        if ([AuditInput]::GetForegroundWindow() -ne $foreground) { throw 'The notification changed focus.' }
        $spoken = @(Get-Content -LiteralPath $speech -Encoding UTF8 | Select-Object -Skip $skip | Where-Object { $_ -match 'Speaking \[' })
        if ($spoken -match ([regex]::Escape($whilePaused))) { throw 'The entry added during the pause was announced.' }
        $events = (Get-Content -LiteralPath $journal -Encoding UTF8 -Raw | ConvertFrom-Json).History | Where-Object Kind -eq 7
        if (($events.Entry -contains $whilePaused) -or ($events.Entry -notcontains $whileActive)) {
            throw 'The activity log did not reflect the protection state.'
        }
        Write-Result 'Verified actual protection off and on. No replay of paused changes. New changes after resuming were spoken and recorded.'
    }
    finally {
        Remove-ItemProperty -LiteralPath $registry -Name $whilePaused,$whileActive -ErrorAction SilentlyContinue
        if (-not $original) { Set-Protection $false }
    }
}

function Expect-UpdateOffer {
    $offer = Wait-For { Window-With '^Aktualizacja PowerOw$' } 'update offer' 24
    if ($elementType::FocusedElement.Current.ProcessId -ne $offer.Current.ProcessId) {
        NativeKey @(0x12,0x09)
        Write-Result 'Used Alt+Tab to focus the visible offer after Windows kept focus in another app.'
    }
    $null = Expect-Focus '^Nie$'
    Wait-Speech "Dostępna jest wersja PowerOw $ExpectedUpdateVersion"
    return $offer
}
function Accept-UpdateOffer {
    $null = Expect-UpdateOffer
    Press '%t'
    $null = Wait-For { Window-With '^Postęp aktualizacji$' } 'download progress dialog'
    Wait-Speech 'Pobieranie aktualizacji'
    Write-Result 'Download progress is available to NVDA.'
}
function Find-TrayIcon {
    for ($i=0; $i -lt 16 -and $elementType::FocusedElement.Current.Name -notmatch 'PowerOw.*Ochrona autostartu'; $i++) {
        if ($elementType::FocusedElement.Current.Name -match 'hidden icons') {
            Press '{ENTER}'
        } elseif ($i % 3 -eq 1) {
            Press '{TAB}'
        } else {
            Press '{RIGHT}'
        }
    }
    Expect-Focus 'PowerOw.*Ochrona autostartu'
}

function Verify-StartupHistory([string[]]$expected) {
    Open-Application
    NativeKey @(0x11,0x48)
    $history = Expect-Focus '^Dziennik działań$'
    $body = Text-Value $history
    if ($body -notmatch 'Wykryto nowy wpis autostartu') { throw 'The detected-entry label is missing.' }
    foreach ($name in $expected) {
        if ($body -notmatch ([regex]::Escape($name))) { throw "Cannot read $name in the activity log." }
    }
    Press '{ESC}'
    Press '%{F4}'
    Write-Result "Read all $($expected.Count) entries from the activity log via Ctrl+H."
}

Set-Content -LiteralPath $record -Encoding UTF8 -Value ("PowerOw audit " + $Stage)
$reader = $null
try {
    if ($Stage -notin 'PrepareUninstall','VerifyUninstall','VerifyUpdate') { $reader = Start-Reader }
    switch ($Stage) {
        'Notifications' { Test-StartupNotifications }
        'StartupSources' { . (Join-Path $PSScriptRoot 'GuestStartupSources.ps1') }
        'VerifyStartupHistory' {
            if (!$StartupProbeTag) { throw 'Set the tag of the completed StartupSources trial.' }
            $journal = Join-Path $env:LOCALAPPDATA 'PowerOw\startup.json'
            $names = (Get-Content -LiteralPath $journal -Encoding UTF8 -Raw | ConvertFrom-Json).History |
                Where-Object { $_.Kind -eq 7 -and $_.Entry -match "$StartupProbeTag|^PowerOw QA MSIX (Desktop|UWP)$" } |
                Select-Object -ExpandProperty Entry -Unique
            if ($names.Count -ne 20) { throw 'The startup source trial has missing activity records.' }
            Restart-Tray
            Verify-StartupHistory $names
        }
        'NotificationsPaused' { Test-NotificationProtection }
        'Setup' {
            if (-not $program -and (Test-Path -LiteralPath $custom) -and -not (Get-ChildItem -LiteralPath $custom -Force)) {
                [IO.Directory]::Delete($custom, $false)
            }
            if (-not $AttachedSetup) {
                $installer = Start-Process -FilePath (Join-Path $root 'PowerOw-Setup.exe') -PassThru
            }
            if (!$AttachedLicense) {
                $languageDialog = Wait-For { Window-With 'Select Setup Language|Język instalacji' } 'language dialog'
                $null = $shell.AppActivate($languageDialog.Current.ProcessId)
                $combo = $languageDialog.FindFirst([Windows.Automation.TreeScope]::Descendants,
                    [Windows.Automation.PropertyCondition]::new($elementType::ControlTypeProperty, $controlType::ComboBox))
                if ($combo) { $combo.SetFocus() }
                Press 'p'
                Press '{ENTER}'
                Wait-Speech 'Witamy w instalatorze aplikacji PowerOw'
                $null = Expect-Focus '^Dalej$'
                Press '{ENTER}'
            } else {
                $licenseWindow = Wait-For { Window-With '^Instalacja - PowerOw$' } 'attached installer license page'
                $null = $shell.AppActivate($licenseWindow.Current.ProcessId)
            }
            Wait-Speech 'Umowy Licencyjnej'
            Press '%a'
            $null = Expect-Focus 'Akceptuję warunki umowy'
            Press '%d'
            $directory = Wait-For { $item = $elementType::FocusedElement; if ((Text-Value $item) -eq $default) { $item } } 'default Program Files folder'
            Write-Result ("Default folder " + (Text-Value $directory))
            Press '^a'
            Press $custom
            if ((Text-Value $directory) -ne $custom) { throw 'The chosen folder did not replace the default.' }
            Press '%d'
            for ($i=0; $i -lt 8 -and $elementType::FocusedElement.Current.ClassName -notmatch 'CheckListBox'; $i++) { Press '{TAB}' }
            $check = $elementType::FocusedElement
            if ($check.Current.ClassName -notmatch 'CheckListBox') { throw ('Unexpected task control ' + $check.Current.ClassName) }
            Wait-Speech 'Utwórz skrót na &pulpicie'
            $expectedIconsAndLogon = $ActivateInstallOptions.IsPresent -or $LeaveInstallerDefaults.IsPresent
            $expectedProtection = $expectedIconsAndLogon -and !$PauseInstallerProtection.IsPresent
            if (!$LeaveInstallerDefaults) {
                Press ' '
                $toggle = $null
                if ($check.TryGetCurrentPattern([Windows.Automation.TogglePattern]::Pattern,[ref]$toggle) -and
                    ($toggle.Current.ToggleState -eq 'On') -ne $expectedIconsAndLogon) {
                    throw 'The desktop shortcut selection did not change as expected.'
                }
                Write-Result "Desktop shortcut toggled with Space. Expected checked: $expectedIconsAndLogon"
                Select-InstallerTask 'Uruchamiaj PowerOw przy logowaniu'
                Select-InstallerTask 'Pilnuj wpisów autostartu'
            } elseif ($PauseInstallerProtection) {
                Select-InstallerTask 'Pilnuj wpisów autostartu'
            }
            Press '%d'
            $null = Expect-Focus '^Instaluj$'
            Press '%i'
            $null = Wait-For {
                (Current-Speech 'Zakończono instalację|Uruchom aplikację PowerOw') -or $elementType::FocusedElement.Current.Name -eq 'Zakończ'
            } 'installation finish' 65
            Write-Result 'Visible finish page'
            $program = (Get-ItemProperty -LiteralPath $installKey).Directory
            if ($program -ne $custom -or -not (Test-Path -LiteralPath (Join-Path $custom 'PowerOw.exe'))) { throw 'The actual installation path differs.' }
            if ((Get-Service -Name PowerOwGuard).Status -ne 'Running') { throw 'The protection service is not running.' }
            if ((Test-Path -LiteralPath "$env:PUBLIC\Desktop\PowerOw.lnk") -ne $expectedIconsAndLogon) {
                throw 'Desktop shortcut does not match the selected option.'
            }
            if (-not (Test-Path -LiteralPath "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\PowerOw.lnk")) { throw 'The Start shortcut is missing.' }
            $task = Get-ScheduledTask -TaskName PowerOw
            if ($task.Settings.Enabled -ne $expectedIconsAndLogon -or $task.Principal.RunLevel -ne 'Highest') {
                throw 'Installer did not save the chosen logon option.'
            }
            $state = Get-Content -LiteralPath (Join-Path $program 'Data\startup.json') -Raw | ConvertFrom-Json
            if ($state.Paused -eq $expectedProtection) { throw 'Installer did not save the chosen protection option.' }
            if ((Get-ItemProperty -LiteralPath $installKey).UpdateTerms -ne 1 -or
                !(Test-Path -LiteralPath (Join-Path $program 'Licenses\Windows-SDK.txt'))) { throw 'SDK terms or license files are missing.' }
            Write-Result "Installed to $program. Start shortcut present. Desktop and logon enabled=$expectedIconsAndLogon. Protection paused=$($state.Paused)."
            if ($SkipLaunch) {
                $null = Wait-For { Current-Speech 'Uruchom aplikację PowerOw' } 'initial focus on launch checkbox'
                Press ' '
            }
            $null = Tab-To '^Zakończ$'
            Press '{ENTER}'
        }
        'App' {
            if ($ExistingProtectionRule) {
                Open-Application
                Focus-Fixture
                Press '^e'
                Accept-Confirmation
                $null = Wait-For { Has-Fixture } 'restored fixture before the repeat audit'
            }
            Put-Fixture
            Open-Application
            Press '{F5}'
            Start-Sleep -Seconds 2
            Focus-Fixture
            Press '{F6}'
            $null = Expect-Focus '^Szczegóły wpisu$'
            if ((Text-Value $elementType::FocusedElement) -notmatch 'HKCU.*CurrentVersion\\Run') { throw 'Details did not show the account registry location.' }
            Focus-Fixture
            $before = @(Get-Content -LiteralPath $speech).Count
            Press '+{F10}'
            Expect-NewSpeech $before "'Wyłącz'"
            Write-Result 'First entry command spoken immediately after Shift+F10.'
            Press '{ESC}'
            $null = Expect-Focus 'Próba dostępności'
            Press '^g'
            Accept-Confirmation
            $null = Wait-For { -not (Has-Fixture) } 'disabled registry value'
            $null = Expect-Focus 'Próba dostępności.*Wyłączony.*Objęty ochroną'
            Put-Fixture
            $null = Wait-For { -not (Has-Fixture) } 'background recurrence prevention' 15
            Start-Sleep -Milliseconds 950
            Press '^h'
            $null = Wait-For { Window-With '^Dziennik działań$' } 'activity log'
            if ((Text-Value $elementType::FocusedElement) -notmatch 'Zablokowano powrót') { throw 'Guard action missing from activity log.' }
            Press '{ESC}'
            Focus-Fixture
            Press '^e'
            Accept-Confirmation
            $null = Wait-For { Has-Fixture } 'restored registry value'
            $null = Expect-Focus 'Próba dostępności.*Zarejestrowany'
            Press '^g'
            Accept-Confirmation
            $null = Wait-For { -not (Has-Fixture) } 'protected fixture for Settings tests'
            Write-Result 'Selection, details, first menu item without arrow, guarded disable, recurrence, history and restore.'
        }
        'Tray' {
            Open-Application
            Press '%{F4}'
            $null = Wait-For { -not (Window-With '^PowerOw v[0-9]') } 'window hidden'
            NativeKey @(0x5B,0x42)
            Write-Result ("Win+B focused " + $elementType::FocusedElement.Current.Name)
            $null = Find-TrayIcon
            $before = @(Get-Content -LiteralPath $speech).Count
            NativeKey @(0x5D)
            Expect-NewSpeech $before "'Otwórz PowerOw'"
            Write-Result 'First tray command spoken immediately after the context-menu key.'
            foreach ($commandName in "'Odśwież'","'Ustawienia","'Sprawdź aktualizacje'","'(Wstrzymaj|Wznów) ochronę'","'Zakończ PowerOw'") {
                $before = @(Get-Content -LiteralPath $speech).Count
                Press '{DOWN}'
                Expect-NewSpeech $before $commandName
            }
            Press '{ESC}'
            $null = Expect-Focus 'PowerOw'
            NativeKey @(0x5D)
            Press '{DOWN}'
            Press '{DOWN}'
            Press '{ENTER}'
            $null = Expect-Focus '^Język interfejsu$'
            Wait-Speech 'Ustawienia PowerOw'
            if (Window-With '^PowerOw v[0-9]') { throw 'Tray settings opened the main window.' }
            Press '{ESC}'
            $null = Wait-For { -not (Window-With '^Ustawienia PowerOw$') } 'settings closed'
            Write-Result 'Settings opened from the tray with focus on the language and kept the main window hidden.'

            NativeKey @(0x5B,0x42)
            $null = Find-TrayIcon
            NativeKey @(0x5D)
            Press '{DOWN}'
            Press '{DOWN}'
            Press '{DOWN}'
            Press '{ENTER}'
            $null = Wait-For { Window-With '^Aktualizacja PowerOw$' } 'tray update result'
            Wait-Speech 'Masz najnowszą dostępną wersję PowerOw.'
            if (Window-With '^PowerOw v[0-9]') { throw 'Tray update check opened the main window.' }
            Press '{ENTER}'
            Write-Result 'NVDA read the result of a public update check from the tray.'

            NativeKey @(0x5B,0x42)
            $null = Find-TrayIcon
            Press '{ENTER}'
            $null = Wait-For { Window-With '^PowerOw v[0-9]' } 'window reopened directly from tray'
            Write-Result 'Keyboard invoked tray menu, focus immediately reached first item, Escape returned, Enter reopened app.'
        }
        'UpdatePaused' {
            $checks = Update-Fixture 'checks.log'
            $prior = if (Test-Path -LiteralPath $checks) { @(Get-Content -LiteralPath $checks).Count } else { 0 }
            Restart-Tray
            Start-Sleep -Seconds 13
            $after = if (Test-Path -LiteralPath $checks) { @(Get-Content -LiteralPath $checks).Count } else { 0 }
            if ($after -ne $prior -or (Window-With '^Aktualizacja PowerOw$')) { throw 'Paused protection performed a background update check.' }
            Write-Result 'No background checks with protection paused and PowerOw in the tray.'
            Open-Application
            $null = Expect-UpdateOffer
            Press '{ENTER}'
            $null = Wait-For { -not (Window-With '^Aktualizacja PowerOw$') } 'declined offer closed'
            if (Get-ChildItem -LiteralPath $program -Directory -Filter 'PowerOw-update-*') { throw 'Declined offer staged files.' }
            Write-Result 'Opening PowerOw notified about the update. Default No accepted by Enter and no installer staged.'
        }
        'UpdateBackground' {
            $null = Update-Fixture 'fixture-session'
            if (!$ExistingUpdateOffer) {
                Open-Application
                NativeKey @(0x11,0xBC)
                $null = Expect-Focus '^Język interfejsu$'
                $guard = Tab-To '^Pilnuj wpisów autostartu$'
                Set-Checkbox $guard $true
                $null = Tab-To '^Częstotliwość sprawdzania w tle$'
                Wait-Speech '15 min'
                Press '{DOWN}'
                Wait-Speech '30 min'
                Write-Result 'Default interval 15 minutes changed to 30 minutes via keyboard.'
                Press '{ENTER}'
                $null = Wait-For { -not (Window-With '^Ustawienia PowerOw$') } 'settings closed'
                Start-Sleep -Seconds 1
                Restart-Tray
            }
            $null = Expect-UpdateOffer
            if (Window-With '^PowerOw v[0-9]') { throw 'Background notification opened the main window.' }
            Write-Result 'NVDA read the background offer without opening the main window.'
            Accept-UpdateOffer
            Wait-Speech 'potwierdź uprawnienia w oknie Windows' 32
            $null = Wait-For { Window-With '^Aktualizacja PowerOw$' } 'message after declining Windows UAC' 60
            Wait-Speech 'Aktualizacja została anulowana'
            Press '{ENTER}'
            Write-Result 'Canceling Windows UAC left the installed version unchanged.'
        }
        'UpdateRetry' {
            if ($ExistingUpdateOffer) {
                $null = Expect-UpdateOffer
                if (Window-With '^PowerOw v[0-9]') { throw 'The background offer opened the main window.' }
                Press '{ENTER}'
                $null = Wait-For { -not (Window-With '^Aktualizacja PowerOw$') } 'background offer dismissed'
                Write-Result 'NVDA read the background offer, and the default No dismissed it before the manual check.'
            }
            Open-Application
            NativeKey @(0x11,0xBC)
            $null = Expect-Focus '^Język interfejsu$'
            $null = Tab-To '^Sprawdź aktualizacje teraz$'
            Press ' '
            Accept-UpdateOffer
            Wait-Speech 'potwierdź uprawnienia w oknie Windows' 32
            Wait-Speech 'Instalowanie PowerOw' 50
            Write-Result 'NVDA read both download and installer progress.'
        }
        'VerifyUpdate' {
            $dll = Get-Item -LiteralPath (Join-Path $program 'PowerOw.dll')
            $version = $ExpectedUpdateVersion.Substring(1)
            if ($dll.VersionInfo.ProductVersion -notmatch ('^' + [regex]::Escape($version) + '(?:\b|$)')) { throw 'Updated application version is missing.' }
            if ((Get-Service PowerOwGuard).Status -ne 'Running') { throw 'Protection service not running after update.' }
            if (!(Get-ScheduledTask -TaskName PowerOw).Settings.Enabled) { throw 'Update changed the logon setting.' }
            $state = Get-Content -LiteralPath (Join-Path $program 'Data\startup.json') -Raw | ConvertFrom-Json
            if ($state.Paused) { throw 'Update changed the protection setting.' }
            $preferences = Get-Content -LiteralPath "$env:LOCALAPPDATA\PowerOw\interface.json" -Raw | ConvertFrom-Json
            if ($preferences.UpdateIntervalMinutes -ne 30) { throw 'Update changed the selected interval.' }
            $null = Wait-For { Window-With ('^PowerOw ' + [regex]::Escape($ExpectedUpdateVersion) + '$') } 'PowerOw reopened after update'
            Write-Result "Update installed $ExpectedUpdateVersion, restarted PowerOw and preserved logon, protection and 30-minute interval."
        }
        'Support' {
            Open-Application
            $null = Tab-To '^Wsparcie i kontakt$'
            Press '{ENTER}'
            $null = Expect-Focus '^Wesprzyj przez PayPal$'
            foreach ($label in 'Wesprzyj przez Revolut','Projekt na GitHubie','Zgłoś problem','Strona autora') {
                Press '{TAB}'
                $null = Expect-Focus ('^' + $label + '$')
            }
            Press '{ESC}'
            $null = Expect-Focus '^Wsparcie i kontakt$'
            Write-Result 'Support destinations reachable by keyboard. Escape restored focus.'
        }
        { $_ -in 'SettingsOff','SettingsOn' } {
            $enable = $Stage -eq 'SettingsOn'
            $expectedLogon = if ($SettingsToChange -ne 'Protection') { $enable } else { (Get-ScheduledTask -TaskName PowerOw).Settings.Enabled }
            $statePath = Join-Path $program 'Data\startup.json'
            $previousPause = (Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json).Paused
            $expectedPause = if ($SettingsToChange -ne 'Logon') { !$enable } else { $previousPause }
            Open-Application
            NativeKey @(0x11,0xBC)
            $null = Expect-Focus '^Język interfejsu$'
            Press '{TAB}'
            $null = Expect-Focus '^Motyw interfejsu$'
            Press '{TAB}'
            $check = Expect-Focus '^Uruchamiaj PowerOw przy logowaniu do Windows$'
            if ($SettingsToChange -ne 'Protection') { Set-Checkbox $check $enable }
            Press '{TAB}'
            $check = Expect-Focus '^Pilnuj wpisów autostartu$'
            if ($SettingsToChange -ne 'Logon') { Set-Checkbox $check $enable }
            Press '{ENTER}'
            $null = Wait-For { -not (Window-With '^Ustawienia PowerOw$') } 'settings closed'
            $null = Wait-For {
                $task = Get-ScheduledTask -TaskName PowerOw
                $task.Settings.Enabled -eq $expectedLogon
            } 'scheduled task state saved'
            $machineState = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
            if ($machineState.Paused -ne $expectedPause) { throw 'The protection state does not match the chosen setting.' }
            if ($ExistingProtectionRule) {
                Put-Fixture
                if (!$expectedPause) {
                    $null = Wait-For { -not (Has-Fixture) } 're-enabled protection blocks returning value' 15
                } else {
                    Start-Sleep -Seconds 9
                    if (-not (Has-Fixture)) { throw 'Paused protection removed the entry.' }
                    Remove-ItemProperty -LiteralPath $registry -Name $fixture
                }
            }
            Write-Result ("Changed {0}. Actual Scheduler Enabled={1}, protection Paused={2}, registry probe={3}." -f $SettingsToChange, $expectedLogon, $expectedPause, $ExistingProtectionRule)
        }
        'PrepareUninstall' {
            if (-not $program) { throw 'PowerOw is not registered.' }
            Set-Content -LiteralPath $uninstallPath -Value $program
            $extra = Join-Path $program 'extra\subfolder'
            New-Item -ItemType Directory -Path $extra -Force | Out-Null
            New-Item -ItemType File -Path (Join-Path $extra 'unknown-file.txt') -Force | Out-Null
            New-Item -ItemType Directory -Path $outside -Force | Out-Null
            Set-Content -LiteralPath $sentinel -Value 'untouched'
            $junction = Join-Path $extra 'link'
            if (-not (Test-Path -LiteralPath $junction)) {
                New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
            }
            if ($TaskMissing -and (Get-ScheduledTask -TaskName PowerOw -ErrorAction SilentlyContinue)) {
                Unregister-ScheduledTask -TaskName PowerOw -Confirm:$false
                Write-Result 'Removed the logon task ahead of uninstall to simulate a partial installation.'
                if (Get-ScheduledTask -TaskName PowerOw -ErrorAction SilentlyContinue) { throw 'Could not remove the logon task.' }
            }
            Write-Result "Prepared extra files and a junction in $program. Start uninstall from Windows Installed apps."
        }
        'ObserveUninstall' {
            $null = Wait-For { Select-String -LiteralPath $speech -SimpleMatch 'Usunąć PowerOw, cały folder instalacji' -Quiet } 'NVDA reading the uninstall confirmation' 120
            Write-Result 'NVDA read the warning about deleting the whole folder.'
            Wait-Speech 'Aplikacja PowerOw została usunięta z komputera.'
        }
        'VerifyUninstall' {
            $program = (Get-Content -LiteralPath $uninstallPath -Raw).Trim()
            $null = Wait-For { -not (Test-Path -LiteralPath $program) } 'installation folder deleted' 45
            if (Get-Service -Name PowerOwGuard -ErrorAction SilentlyContinue) { throw 'Service remains.' }
            if (Get-ScheduledTask -TaskName PowerOw -ErrorAction SilentlyContinue) { throw 'Logon task remains.' }
            if ((Get-Content -LiteralPath $sentinel -Raw).Trim() -ne 'untouched') { throw 'The junction target was affected.' }
            if (Test-Path -LiteralPath "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\PowerOw.lnk") { throw 'The Start shortcut remains.' }
            if (Test-Path -LiteralPath "$env:PUBLIC\Desktop\PowerOw.lnk") { throw 'The desktop shortcut remains.' }
            if (Test-Path -LiteralPath $installKey) { throw 'The installation registry key remains.' }
            if (Get-Process -Name PowerOw -ErrorAction SilentlyContinue) { throw 'The app kept running.' }
            Write-Result "Removed $program with nested extra files, both shortcuts, task, service and registry key. PowerOw stopped and the junction target was preserved."
            $defender = Get-MpComputerStatus
            Write-Result "Defender real-time protection=$($defender.RealTimeProtectionEnabled). Signatures $($defender.AntivirusSignatureVersion)."
        }
    }
    Write-Result 'PASS'
}
catch {
    Write-Result ("FAIL " + $_ + " at " + $_.ScriptStackTrace)
    try {
        $current = $elementType::FocusedElement
        Write-Result ("Current focus " + $current.Current.Name + " — " + $current.Current.ControlType.ProgrammaticName)
    } catch { }
    throw
}
finally {
    if ($reader -and -not $reader.HasExited) { Stop-Process -Id $reader.Id -ErrorAction SilentlyContinue }
}
