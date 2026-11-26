#ifndef Payload
  #error Define the publish directory with /DPayload.
#endif
#ifndef OutputFolder
  #error Define the build output directory with /DOutputFolder.
#endif
#ifndef LanguageMessages
  #error Define the custom messages include with /DLanguageMessages.
#endif
#ifndef LocationHelper
  #error Define the location verification tool with /DLocationHelper.
#endif
#ifndef Version
  #error Define the release version with /DVersion.
#endif
#ifndef LicensePages
  #error Define the generated license pages with /DLicensePages.
#endif

[Setup]
AppId=Proszczok.PowerOw
AppName=PowerOw
AppVersion={#Version}
AppPublisher=Proszczok
AppCopyright=Copyright (c) 2026 Proszczok
VersionInfoCopyright=Copyright (c) 2026 Proszczok
VersionInfoVersion={#Version}.0
VersionInfoDescription=PowerOw Setup
DefaultDirName={commonpf64}\PowerOw
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousLanguage=no
ShowLanguageDialog=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19045
UninstallDisplayIcon={app}\PowerOw.exe
UninstallDisplayName=PowerOw
SetupIconFile=..\assets\PowerOw.ico
WizardImageFile=..\assets\PowerOw-Setup.bmp
WizardSmallImageFile=..\assets\PowerOw-Setup-Small.bmp
OutputDir={#OutputFolder}
OutputBaseFilename=PowerOw-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
CloseApplications=no
RestartApplications=no
SetupMutex=Proszczok.PowerOw.Setup

[Languages]
Name: pl; MessagesFile: "compiler:Languages\Polish.isl"; LicenseFile: "{#LicensePages}\pl.txt"
Name: en; MessagesFile: "compiler:Default.isl"; LicenseFile: "{#LicensePages}\en.txt"
Name: de; MessagesFile: "compiler:Languages\German.isl"; LicenseFile: "{#LicensePages}\de.txt"
Name: ru; MessagesFile: "compiler:Languages\Russian.isl"; LicenseFile: "{#LicensePages}\ru.txt"
Name: hr; MessagesFile: "Languages\Croatian.isl"; LicenseFile: "{#LicensePages}\hr.txt"
Name: fr; MessagesFile: "compiler:Languages\French.isl"; LicenseFile: "{#LicensePages}\fr.txt"
Name: es; MessagesFile: "compiler:Languages\Spanish.isl"; LicenseFile: "{#LicensePages}\es.txt"
Name: zh; MessagesFile: "Languages\ChineseSimplified.isl"; LicenseFile: "{#LicensePages}\zh.txt"

#include LanguageMessages

[Tasks]
Name: desktopicon; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: logon; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:SetupOptions}"; Flags: checkedonce
Name: protection; Description: "{cm:EnableProtection}"; GroupDescription: "{cm:SetupOptions}"; Flags: checkedonce

[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace uninsrestartdelete
Source: "installed.marker"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#LocationHelper}"; Flags: dontcopy

[Icons]
Name: "{commonprograms}\PowerOw"; Filename: "{app}\PowerOw.exe"; WorkingDir: "{app}"
Name: "{commondesktop}\PowerOw"; Filename: "{app}\PowerOw.exe"; WorkingDir: "{app}"; Tasks: desktopicon; Check: RegularInstall

[Registry]
Root: HKLM64; Subkey: "Software\Proszczok\PowerOw"; ValueType: string; ValueName: "Directory"; ValueData: "{app}"; Flags: uninsdeletekey

[INI]
Filename: "{app}\Appearance.ini"; Section: "Interface"; Key: "Language"; String: "{code:PreferenceLanguage}"; Check: RegularInstall
Filename: "{app}\Appearance.ini"; Section: "Interface"; Key: "Theme"; String: "system"; Check: RegularInstall

[Run]
Filename: "{app}\PowerOw.exe"; Description: "{cm:LaunchProgram,PowerOw}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent runascurrentuser; Check: GuardInstalled
Filename: "{app}\PowerOw.exe"; Parameters: "{code:RestartArguments}"; WorkingDir: "{app}"; Flags: nowait runascurrentuser; Check: RestartAfterUpdate

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  FileMissing = $FFFFFFFF;
  IsDirectory = $10;
  IsReparsePoint = $400;
  InstallKey = 'Software\Proszczok\PowerOw';
  TermsVersion = 1;

var
  GuardExit: Integer;
  VerifiedRemovalPath: String;
  QueuedRemoval: Boolean;
  Updating: Boolean;
  ProgressText: TNewEdit;
  DisplayedPercent: Integer;

function FileAttributes(Name: String): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';
function UnlinkDirectory(Name: String): Boolean;
  external 'RemoveDirectoryW@kernel32.dll stdcall';
function UnlinkFile(Name: String): Boolean;
  external 'DeleteFileW@kernel32.dll stdcall';
function DeleteAtRestart(Name: String; Destination: HWND; Flags: Cardinal): Boolean;
  external 'MoveFileExW@kernel32.dll stdcall';
procedure NotifyWinEvent(Event: Cardinal; Window: HWND; ObjectId, ChildId: Integer);
  external 'NotifyWinEvent@user32.dll stdcall';

function InitializeSetup: Boolean;
var
  Terms: Cardinal;
  Installed: String;
begin
  Updating := ExpandConstant('{param:POWEROWUPDATE|0}') = '1';
  Result := not WizardSilent;
  if WizardSilent then
    Result := RegQueryDWordValue(HKLM64, InstallKey, 'UpdateTerms', Terms) and (Terms = TermsVersion) and
      RegQueryStringValue(HKLM64, InstallKey, 'Directory', Installed) and
      FileExists(AddBackslash(Installed) + 'installed.marker');
  if Updating and not WizardSilent then Result := False;
  if not Result then MsgBox(CustomMessage('SetupAcceptTerms'), mbError, MB_OK);
end;

function RegularInstall: Boolean;
begin
  Result := not Updating;
end;

function RestartAfterUpdate: Boolean;
begin
  Result := Updating and (GuardExit = 0);
end;

function RestartArguments(Param: String): String;
begin
  if ExpandConstant('{param:POWEROWTRAY|0}') = '1' then Result := '--tray' else Result := '';
end;

function ContainsReparse(Directory: String; RemoveLinks: Boolean): Boolean;
var
  Item: TFindRec;
  Path: String;
begin
  Result := False;
  if FindFirst(AddBackslash(Directory) + '*', Item) then
  try
    repeat
      if (Item.Name <> '.') and (Item.Name <> '..') then
      begin
        Path := AddBackslash(Directory) + Item.Name;
        if (Item.Attributes and IsReparsePoint) <> 0 then
        begin
          if RemoveLinks then
          begin
            if (Item.Attributes and IsDirectory) <> 0 then
              Result := not UnlinkDirectory(Path)
            else
              Result := not UnlinkFile(Path);
          end
          else
            Result := True;
        end
        else if (Item.Attributes and IsDirectory) <> 0 then
          Result := ContainsReparse(Path, RemoveLinks);
      end;
    until Result or not FindNext(Item);
  finally
    FindClose(Item);
  end;
end;

function SafeUninstallPath: Boolean;
var
  Path, Registered, Parent: String;
  Attributes: Cardinal;
begin
  Path := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));
  Result := False;
  if not RegQueryStringValue(HKLM64, InstallKey, 'Directory', Registered) then Exit;
  if (Length(Path) < 4) or (CompareText(Path, Registered) <> 0) then Exit;
  if not FileExists(AddBackslash(Path) + 'installed.marker') then Exit;
  Parent := Path;
  while Parent <> '' do
  begin
    Attributes := FileAttributes(Parent);
    if (Attributes = FileMissing) or ((Attributes and IsDirectory) = 0) or
      ((Attributes and IsReparsePoint) <> 0) then Exit;
    if Length(Parent) <= 3 then Break;
    Parent := ExtractFileDir(Parent);
  end;
  Result := True;
end;

function PreferenceLanguage(Param: String): String;
begin
  if ActiveLanguage = 'zh' then
    Result := 'zh-CN'
  else
    Result := ActiveLanguage;
end;

function GuardInstalled: Boolean;
begin
  Result := GuardExit = 0;
end;

function RunMaintenance(Switch: String; var ExitCode: Integer): Boolean;
begin
  ExitCode := 0;
  Result := Exec(ExpandConstant('{app}\PowerOw.exe'), Switch, ExpandConstant('{app}'),
    SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
  if not Result then
    Log(Format('PowerOw maintenance %s returned %d', [Switch, ExitCode]));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  Chosen, Registered: String;
begin
  Result := '';
  Chosen := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));
  if RegQueryStringValue(HKLM64, InstallKey, 'Directory', Registered) and (CompareText(Chosen, Registered) <> 0) then
  begin
    Result := CustomMessage('SetupExistingInstall');
    Exit;
  end;
  ExtractTemporaryFile('PowerOw.LocationCheck.exe');
  if not Exec(ExpandConstant('{tmp}\PowerOw.LocationCheck.exe'), '"' + Chosen + '"',
    ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, ExitCode) then ExitCode := 3;
  if (ExitCode <> 0) or ContainsReparse(Chosen, False) then
  begin
    Log(Format('Location check returned %d', [ExitCode]));
    if ExitCode = 4 then
      Result := CustomMessage('SetupOccupiedPath')
    else
      Result := CustomMessage('SetupUnsafePath');
    Exit;
  end;
  if FileExists(ExpandConstant('{app}\PowerOw.exe')) then
    if not RunMaintenance('--setup-stop', ExitCode) then
      Result := CustomMessage('SetupProtectionError');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExitCode: Integer;
  DesktopLink, Options: String;
  Shell, Shortcut: Variant;
begin
  if CurStep = ssPostInstall then
  begin
    Options := '--setup-install';
    if not Updating then
    begin
      Options := Options + ' --logon=';
      if WizardIsTaskSelected('logon') then Options := Options + '1' else Options := Options + '0';
      Options := Options + ' --protection=';
      if WizardIsTaskSelected('protection') then Options := Options + '1' else Options := Options + '0';
    end;
    if not RunMaintenance(Options, ExitCode) then
    begin
      GuardExit := 20;
      SuppressibleMsgBox(CustomMessage('SetupProtectionError'), mbError, MB_OK, IDOK);
    end
    else
      RegWriteDWordValue(HKLM64, InstallKey, 'UpdateTerms', TermsVersion);
    if (not Updating) and (not WizardIsTaskSelected('desktopicon')) then
    begin
      DesktopLink := ExpandConstant('{commondesktop}\PowerOw.lnk');
      if FileExists(DesktopLink) then
      try
        Shell := CreateOleObject('WScript.Shell');
        Shortcut := Shell.CreateShortcut(DesktopLink);
        if CompareText(Shortcut.TargetPath, ExpandConstant('{app}\PowerOw.exe')) = 0 then
          DeleteFile(DesktopLink);
      except
        Log('The existing desktop shortcut could not be inspected.');
      end;
    end;
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  Result := GuardExit;
end;

function InitializeUninstall: Boolean;
begin
  Result := SafeUninstallPath;
  if not Result then
    SuppressibleMsgBox(CustomMessage('SetupUnsafePath'), mbError, MB_OK, IDOK);
end;

procedure ScheduleRemainder(Path: String);
var
  Item: TFindRec;
  Child: String;
begin
  if FindFirst(AddBackslash(Path) + '*', Item) then
  try
    repeat
      if (Item.Name <> '.') and (Item.Name <> '..') then
      begin
        Child := AddBackslash(Path) + Item.Name;
        if ((Item.Attributes and IsDirectory) <> 0) and ((Item.Attributes and IsReparsePoint) = 0) then
          ScheduleRemainder(Child)
        else if DeleteAtRestart(Child, 0, 4) then
          QueuedRemoval := True;
      end;
    until not FindNext(Item);
  finally
    FindClose(Item);
  end;
  if DeleteAtRestart(Path, 0, 4) then
    QueuedRemoval := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ExitCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    if (not SafeUninstallPath) or ContainsReparse(ExpandConstant('{app}'), True) then
      RaiseException(CustomMessage('SetupUnsafePath'));
    if not RunMaintenance('--setup-remove', ExitCode) then
      RaiseException(CustomMessage('SetupProtectionError'));
    VerifiedRemovalPath := ExpandConstant('{app}');
  end;
  if (CurUninstallStep = usPostUninstall) and (VerifiedRemovalPath <> '') and DirExists(VerifiedRemovalPath) then
    if (FileAttributes(VerifiedRemovalPath) and IsReparsePoint) = 0 then
      ScheduleRemainder(VerifiedRemovalPath);
end;

function UninstallNeedRestart: Boolean;
begin
  Result := QueuedRemoval;
end;

procedure InitializeWizard;
begin
  WizardForm.ProgressGauge.TabStop := True;
  ProgressText := TNewEdit.Create(WizardForm);
  ProgressText.Parent := WizardForm.InstallingPage;
  ProgressText.Left := WizardForm.ProgressGauge.Left;
  ProgressText.Top := WizardForm.ProgressGauge.Top + WizardForm.ProgressGauge.Height + ScaleY(8);
  ProgressText.Width := WizardForm.ProgressGauge.Width;
  ProgressText.ReadOnly := True;
  ProgressText.Text := Format(CustomMessage('SetupInstallProgress'), [0]);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpInstalling then WizardForm.ActiveControl := ProgressText;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
var
  Percent: Integer;
begin
  if (MaxProgress <= 0) or (ProgressText = nil) then Exit;
  Percent := Round(CurProgress * 100.0 / MaxProgress);
  if (Percent >= DisplayedPercent + 5) or ((Percent = 100) and (DisplayedPercent < 100)) then
  begin
    DisplayedPercent := Percent;
    ProgressText.Text := Format(CustomMessage('SetupInstallProgress'), [Percent]);
    if ProgressText.Focused then
      NotifyWinEvent($800E, ProgressText.Handle, -4, 0);
  end;
end;
