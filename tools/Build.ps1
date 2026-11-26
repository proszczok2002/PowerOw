[CmdletBinding()]
param(
    [string]$Dotnet = 'dotnet',
    [string]$Iscc,
    [string]$OutputDirectory,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
if (!$Version) {
    $project = [xml][IO.File]::ReadAllText((Join-Path $root 'PowerOw.csproj'))
    $Version = $project.Project.PropertyGroup.Version
}
if (-not $Iscc) {
    $portable = Join-Path $root 'robocze\tooling\Inno\ISCC.exe'
    $Iscc = if (Test-Path -LiteralPath $portable) { $portable } else { 'ISCC.exe' }
}
$build = Join-Path (Join-Path $root 'robocze\build') ([Guid]::NewGuid().ToString('N'))
$payload = Join-Path $build 'powerow-publish'
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'dist' }
New-Item -ItemType Directory -Path $build,$output -Force | Out-Null
function Invoke-BuildTool {
    param([string]$Tool, [string[]]$Arguments)
    & $Tool @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Tool returned $LASTEXITCODE" }
}

Invoke-BuildTool $Dotnet @('publish', (Join-Path $root 'PowerOw.csproj'), '-c', 'Release', '-r', 'win-x64',
    '--self-contained', 'true', "-p:Version=$Version", '-p:DebugType=None', '-p:DebugSymbols=false', '--disable-build-servers', '-o', $payload)
$helper = Join-Path $build 'location'
Invoke-BuildTool $Dotnet @('build', (Join-Path $root 'installer\LocationCheck.csproj'), '-c', 'Release',
    '-p:DebugType=None', '-p:DebugSymbols=false', '--disable-build-servers', '-o', $helper)
$consent = Join-Path $build 'consent'
Invoke-BuildTool $Dotnet @('build', (Join-Path $root 'installer\UpdateConsent\UpdateConsent.csproj'), '-c', 'Release',
    '-p:DebugType=None', '-p:DebugSymbols=false', '--disable-build-servers', '-o', $consent)
Copy-Item -LiteralPath (Join-Path $consent 'PowerOw.UpdateConsent.exe') -Destination $payload

$notices = Join-Path $payload 'Licenses'
New-Item -ItemType Directory -Path $notices | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $notices 'PowerOw-MIT.txt')
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $notices
Copy-Item -Path (Join-Path $root 'licenses\*.txt') -Destination $notices
$dependencyFile = Get-Content -LiteralPath (Join-Path $payload 'PowerOw.deps.json') -Raw | ConvertFrom-Json
$assetsFile = Get-Content -LiteralPath (Join-Path $root 'obj\project.assets.json') -Raw | ConvertFrom-Json
foreach ($entry in $dependencyFile.libraries.PSObject.Properties.Name) {
    $identity, $resolvedVersion = ($entry -replace '^runtimepack\.', '') -split '/', 2
    if ($identity -notmatch '^(Microsoft\.(NETCore|WindowsDesktop)\.App\.Runtime\.win-x64|System\.ServiceProcess\.ServiceController|System\.Diagnostics\.EventLog)$') { continue }
    $folders = @($assetsFile.packageFolders.PSObject.Properties.Name)
    $relative = Join-Path $identity.ToLowerInvariant() $resolvedVersion
    $package = $folders | ForEach-Object { Join-Path $_ $relative } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$package) { throw "Cannot locate the notices for $identity $resolvedVersion" }
    foreach ($notice in Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -match '^(LICENSE|THIRD.PARTY.NOTICES)(\.TXT)?$' }) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $notices "$identity-$($notice.BaseName).txt")
    }
}
if (!(Test-Path -LiteralPath (Join-Path $notices 'Microsoft.NETCore.App.Runtime.win-x64-LICENSE.txt')) -or
    !(Test-Path -LiteralPath (Join-Path $notices 'Microsoft.WindowsDesktop.App.Runtime.win-x64-LICENSE.txt'))) {
    throw 'Missing .NET runtime license files.'
}

$languageNames = 'en','pl','de','ru','hr','fr','es','zh'
$messages = @{}
foreach ($catalog in 'Setup.tsv','Interface.tsv') {
    foreach ($line in [IO.File]::ReadLines((Join-Path $root "assets\Translations\$catalog"))) {
        $columns = $line.Split("`t")
        if ($columns.Length -eq 9) { $messages[$columns[0]] = $columns }
    }
}
$content = [Collections.Generic.List[string]]::new()
$content.Add('[CustomMessages]')
for ($i = 0; $i -lt $languageNames.Length; $i++) {
    foreach ($key in 'SetupUnsafePath','SetupProtectionError','SetupOccupiedPath','SetupExistingInstall','SetupOptions','StartWithWindows',
        'EnableProtection','SetupAcceptTerms','SetupInstallProgress') {
        $content.Add("$($languageNames[$i]).$key=$($messages[$key][$i + 1])")
    }
}
$content.Add('[Messages]')
for ($i = 0; $i -lt $languageNames.Length; $i++) {
    $content.Add("$($languageNames[$i]).ConfirmUninstall=$($messages['SetupUninstall'][$i + 1])")
}
$include = Join-Path $build 'Messages.isinc'
[IO.File]::WriteAllLines($include, $content, [Text.UTF8Encoding]::new($true))
$licensePages = Join-Path $build 'license-pages'
New-Item -ItemType Directory -Path $licensePages | Out-Null
$sdkTerms = [IO.File]::ReadAllText((Join-Path $root 'licenses\Windows-SDK.txt'))
for ($i = 0; $i -lt $languageNames.Length; $i++) {
    $intro = $messages['SdkAgreement'][$i + 1].Replace('\n', [Environment]::NewLine)
    [IO.File]::WriteAllText((Join-Path $licensePages ($languageNames[$i] + '.txt')),
        $intro + [Environment]::NewLine + [Environment]::NewLine + $sdkTerms, [Text.UTF8Encoding]::new($true))
}
$location = Join-Path $helper 'PowerOw.LocationCheck.exe'
Invoke-BuildTool $Iscc @("/DPayload=$payload", "/DOutputFolder=$build", "/DLanguageMessages=$include", "/DLocationHelper=$location",
    "/DVersion=$Version", "/DLicensePages=$licensePages", (Join-Path $root 'installer\PowerOw.iss'))
$setup = Join-Path $output 'PowerOw-Setup.exe'
Copy-Item -LiteralPath (Join-Path $build 'PowerOw-Setup.exe') -Destination $setup -Force
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), "$hash  PowerOw-Setup.exe`r`n", [Text.Encoding]::ASCII)
Get-Item -LiteralPath $setup | Select-Object FullName,Length
Write-Output $hash
