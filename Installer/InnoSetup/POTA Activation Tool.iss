; ===================================================================
;  POTA Activation Tool (PAT) - installer
;
;  Build with Inno Setup 6 or newer (run from the repo root):
;      iscc "Installer\InnoSetup\POTA Activation Tool.iss"
;
;  Expects the published self-contained single-file exe and its loose
;  data files at:
;      publish\POTA Activation Tool.exe
;      publish\counties.json
;      publish\HelpContent.rtf
;      publish\ParkElevations.csv
;      publish\cqZones.json
;      publish\ituZones.json
;  (build.ps1, at the repo root, puts them there)
; ===================================================================

#define MyAppName "POTA Activation Tool"
#define MyAppPublisher "K5JSG"
#define MyAppURL "https://github.com/K5JSG/POTA-Activation-Tool"
#define MyAppExeName "POTA Activation Tool.exe"
; The product's name from 1.4.0 until the rename in 1.15.0 - see [Code].
#define OldAppName "POTA Activator Park Activations"
; This installer's AppId (see [Setup]), also used by [Code] to tell its own
; Installed Apps entry apart from older copies.
#define MyAppId "{{62A39724-A285-4A61-A519-B6C98BA075B2}"

; Overridable from the command line: iscc /DMyAppVersion=1.4.0 ...
#ifndef MyAppVersion
  #define MyAppVersion "1.4.0"
#endif

[Setup]
; Keep this GUID stable forever: it is how Windows recognises an upgrade of
; the same product rather than a second installation. Deliberately a fresh
; GUID, unrelated to the old Visual Studio Installer Projects (.vdproj)
; build's ProductCode/UpgradeCode - that was a different installer
; technology entirely, and its ProductCode changed on every release anyway
; (VS Installer Projects regenerates it per version, unlike this one).
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#MyAppVersion}

DefaultDirName={autopf}\{#MyAppPublisher}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Never offered to the user (no group page), so always use the current
; name - otherwise an upgrade from before the rename would keep the Start
; menu folder under the old name.
UsePreviousGroup=no
DisableDirPage=no
AllowNoIcons=yes
LicenseFile=..\..\License.txt

; Writing to Program Files needs admin - the app itself does not require
; elevation to run (see GetWritableAppDataFolder in Form1.cs, which is
; exactly why %LocalAppData% is used for all user data), just to install.
PrivilegesRequired=admin

OutputDir=..\..\dist
OutputBaseFilename={#MyAppName} Setup {#MyAppVersion}
SetupIconFile=..\..\logo.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Windows 10 1809 or newer
MinVersion=10.0.17763

; Offer to shut the app down instead of demanding a reboot
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; \
    GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\publish\counties.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\..\publish\HelpContent.rtf"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\..\publish\ParkElevations.csv"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\..\publish\cqZones.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\..\publish\ituZones.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\..\License.txt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
; The desktop shortcut must come BEFORE the Start menu ones. When it was
; created after them, every upgrade made Explorer drop the desktop icon into
; the next free spot instead of leaving it where the user had put it.
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; \
    Flags: postinstall nowait skipifsilent

[UninstallDelete]
; The desktop shortcut isn't always in this install's uninstall log: an
; upgrade with the desktop icon task off (Ham Program Auto Update installs
; with /MERGETASKS=!desktopicon) keeps the existing shortcut instead of
; making it, and one renamed from the old product name (see
; RenameFromOldName) never was. Removed by name on uninstall either way.
Type: files; Name: "{autodesktop}\{#MyAppName}.lnk"

[Code]

// Updates install IN PLACE over the existing copy (same AppId), so Installed
// Apps keeps one entry that just shows the new version number, and the
// desktop and Start menu shortcuts stay where they are. Before the new files
// go in:
//   - every OTHER installed copy of this app is silently uninstalled: the old
//     Visual Studio Installer Projects MSI builds, including the ones from
//     before the rename, when the product was called "POTA Check", plus any
//     stray entry registered somewhere other than this installer's own;
//   - the program folder is emptied (all but the uninstaller), so no file
//     that an older version shipped and this one dropped is left behind.
// An upgrade from before the rename (when this was "POTA Activator Park
// Activations") also moves the program folder, desktop shortcut and Start
// menu folder to the new name - see RenameFromOldName.
// User data lives in %LocalAppData% (see GetWritableAppDataFolder in
// Form1.cs) and is never touched, so caches and offline maps survive.

const
  UninstallKeyRoot = 'Software\Microsoft\Windows\CurrentVersion\Uninstall';
  LegacyAppName = 'POTA Check';
var
  OldInstallDirs: TArrayOfString;
  // The previous install's folder when it was still named after the old
  // product name - removed once the new folder is installed.
  RenamedFromDir: String;

function IsOurProductName(const Name: String): Boolean;
begin
  Result := (CompareText(Name, '{#MyAppName}') = 0) or
            (CompareText(Name, '{#OldAppName}') = 0) or
            (CompareText(Name, LegacyAppName) = 0);
end;

// This installer's own Installed Apps entry: the one an upgrade updates in
// place instead of removing. Admin installs in 64-bit mode register it under
// HKLM's 64-bit view.
function IsOwnEntry(RootKey: Integer; const SubkeyName: String): Boolean;
begin
  Result := (RootKey = HKLM64) and
            (CompareText(SubkeyName, ExpandConstant('{#MyAppId}_is1')) = 0);
end;

function SameDir(const A, B: String): Boolean;
begin
  Result := CompareText(RemoveBackslashUnlessRoot(A), RemoveBackslashUnlessRoot(B)) = 0;
end;

procedure RememberOldInstallDir(const Dir: String);
var
  N: Integer;
begin
  if Dir = '' then Exit;
  N := GetArrayLength(OldInstallDirs);
  SetArrayLength(OldInstallDirs, N + 1);
  OldInstallDirs[N] := Dir;
end;

// Only ever touches a folder that is clearly this app's own (named after the
// product), never some general-purpose folder the user may have picked on
// the directory page.
function IsOurAppFolder(const Dir: String): Boolean;
begin
  Result := (Dir <> '') and DirExists(Dir) and
            (IsOurProductName(ExtractFileName(Dir)));
end;

procedure DeleteAppFolder(Dir: String);
begin
  Dir := RemoveBackslashUnlessRoot(Dir);
  if IsOurAppFolder(Dir) then
  begin
    DelTree(Dir, True, True, True);
    // The K5JSG publisher folder above it - RemoveDir only succeeds if empty
    RemoveDir(ExtractFileDir(Dir));
  end;
end;

// An Inno uninstaller re-launches itself from %TEMP% and deletes its own
// unins*.exe as the very last step, so this confirms it has really finished
// before the new files go in.
procedure WaitForFileGone(const FileName: String; TimeoutMs: Integer);
begin
  while FileExists(FileName) and (TimeoutMs > 0) do
  begin
    Sleep(250);
    TimeoutMs := TimeoutMs - 250;
  end;
end;

// Silently uninstalls every installed copy of this app registered under
// RootKey, except this installer's own entry (see IsOwnEntry). Returns an
// error message, or '' if everything went fine.
function UninstallOldVersions(RootKey: Integer): String;
var
  Names: TArrayOfString;
  I, ResultCode: Integer;
  Key, DisplayName, Publisher, Location, Uninstaller: String;
  IsMsi: Cardinal;
begin
  Result := '';
  if not RegGetSubkeyNames(RootKey, UninstallKeyRoot, Names) then Exit;

  for I := 0 to GetArrayLength(Names) - 1 do
  begin
    Key := UninstallKeyRoot + '\' + Names[I];
    if not IsOwnEntry(RootKey, Names[I]) and
       RegQueryStringValue(RootKey, Key, 'DisplayName', DisplayName) and
       IsOurProductName(DisplayName) and
       RegQueryStringValue(RootKey, Key, 'Publisher', Publisher) and
       (CompareText(Publisher, '{#MyAppPublisher}') = 0) then
    begin
      if RegQueryStringValue(RootKey, Key, 'InstallLocation', Location) then
        RememberOldInstallDir(Location);

      if RegQueryDWordValue(RootKey, Key, 'WindowsInstaller', IsMsi) and (IsMsi = 1) then
      begin
        // An MSI build: the subkey name is its ProductCode, and it may not
        // have recorded InstallLocation, so fall back to the usual folder.
        RememberOldInstallDir(ExpandConstant('{autopf}\{#MyAppPublisher}\') + DisplayName);
        if not Exec(ExpandConstant('{sys}\msiexec.exe'),
                    '/x ' + Names[I] + ' /qn /norestart',
                    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
          ResultCode := -1;
        // 1605 = already gone, 3010 = done but wants a reboot
        if (ResultCode <> 0) and (ResultCode <> 1605) and (ResultCode <> 3010) then
        begin
          Result := Format('%s could not be removed automatically (error %d).', [DisplayName, ResultCode]);
          Exit;
        end;
      end
      else if RegQueryStringValue(RootKey, Key, 'UninstallString', Uninstaller) then
      begin
        Uninstaller := RemoveQuotes(Uninstaller);
        RememberOldInstallDir(ExtractFileDir(Uninstaller));
        if FileExists(Uninstaller) then
        begin
          if not Exec(Uninstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART',
                      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
            ResultCode := -1;
          if ResultCode <> 0 then
          begin
            Result := Format('%s could not be removed automatically (error %d).', [DisplayName, ResultCode]);
            Exit;
          end;
          WaitForFileGone(Uninstaller, 60000);
        end;
        // An entry whose uninstaller is missing (folder deleted by hand)
        // would otherwise linger in Installed Apps forever.
        if RegKeyExists(RootKey, Key) then
          RegDeleteKeyIncludingSubkeys(RootKey, Key);
      end;
    end;
  end;
end;

// Removes every other installed copy (see UninstallOldVersions) and whatever
// their uninstallers left behind. Returns an error message for
// PrepareToInstall, or '' if everything went fine.
function RemoveOtherCopies(): String;
var
  I: Integer;
begin
  Result := UninstallOldVersions(HKLM64);
  if Result = '' then Result := UninstallOldVersions(HKLM32);
  if Result = '' then Result := UninstallOldVersions(HKCU);

  if Result <> '' then
  begin
    Result := Result + #13#10#13#10 +
      'Please uninstall it from Settings > Apps > Installed apps, then run this setup again.';
    Exit;
  end;

  // Files the old uninstallers didn't remove themselves. The folder being
  // upgraded is left to CleanAppFolder, which keeps its uninstaller.
  for I := 0 to GetArrayLength(OldInstallDirs) - 1 do
    if not SameDir(OldInstallDirs[I], ExpandConstant('{app}')) then
      DeleteAppFolder(OldInstallDirs[I]);
end;

// What the folder cleanup below leaves in place: the uninstaller
// (unins000.exe/.dat/.msg), which the upgraded copy carries on using.
function KeepOnUpgrade(const Name: String): Boolean;
begin
  Result := (CompareText(Copy(Name, 1, 5), 'unins') = 0);
end;

// Empties the program folder just before the new version's files are copied
// in, so nothing an older version installed and this one no longer ships is
// left behind.
procedure CleanAppFolder();
var
  Dir: String;
  FindRec: TFindRec;
begin
  Dir := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));
  if not IsOurAppFolder(Dir) then Exit;

  if FindFirst(Dir + '\*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') and not KeepOnUpgrade(FindRec.Name) then
        begin
          if FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0 then
            DelTree(Dir + '\' + FindRec.Name, True, True, True)
          else
            DeleteFile(Dir + '\' + FindRec.Name);
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

// ---- Upgrading from before the rename. The AppId is the same, so it's an
// in-place upgrade of the same Installed Apps entry, but Inno would reuse
// the old program folder and shortcuts are named after the product.

// Runs for silent installs too (the wizard exists, just isn't shown).
// Inno has already filled in the previous install's folder; if that folder
// is the old product name, install next to it under the new name instead.
// A folder the user picked themselves (any other name) is left alone.
procedure InitializeWizard();
var
  Dir: String;
begin
  RenamedFromDir := '';
  Dir := RemoveBackslashUnlessRoot(WizardForm.DirEdit.Text);
  if CompareText(ExtractFileName(Dir), '{#OldAppName}') = 0 then
  begin
    RenamedFromDir := Dir;
    WizardForm.DirEdit.Text := AddBackslash(ExtractFileDir(Dir)) + '{#MyAppName}';
  end;
end;

// The desktop shortcut is renamed in place rather than deleted and made
// again under the new name, so Explorer keeps it where the user put it
// (a new file name would land in the next free spot). Its target still
// points into the old program folder, which is deleted after the install,
// so RetargetDesktopShortcut points it at the new exe afterwards - Inno
// itself doesn't rewrite it when the desktop icon task is off (Ham Program
// Auto Update's silent upgrades).
procedure RenameDesktopShortcut(const Desktop: String);
var
  OldLink, NewLink: String;
begin
  OldLink := AddBackslash(Desktop) + '{#OldAppName}.lnk';
  NewLink := AddBackslash(Desktop) + '{#MyAppName}.lnk';
  if FileExists(OldLink) and not FileExists(NewLink) then
    RenameFile(OldLink, NewLink);
end;

// Points a desktop shortcut that still targets the old exe at the new one,
// editing the existing .lnk (only its target, working folder and icon) so
// it keeps its place on the desktop. Best effort - a failure here just
// leaves a shortcut the user can delete.
procedure RetargetDesktopShortcut(const Desktop: String);
var
  Link: String;
  Shell, Shortcut: Variant;
begin
  Link := AddBackslash(Desktop) + '{#MyAppName}.lnk';
  if not FileExists(Link) then Exit;
  try
    Shell := CreateOleObject('WScript.Shell');
    Shortcut := Shell.CreateShortcut(Link);
    if CompareText(ExtractFileName(Shortcut.TargetPath), '{#OldAppName}.exe') <> 0 then Exit;
    Shortcut.TargetPath := ExpandConstant('{app}\{#MyAppExeName}');
    Shortcut.WorkingDirectory := ExpandConstant('{app}');
    Shortcut.IconLocation := ExpandConstant('{app}\{#MyAppExeName}') + ',0';
    Shortcut.Save;
  except
  end;
end;

procedure RenameFromOldName();
begin
  RenameDesktopShortcut(ExpandConstant('{commondesktop}'));
  RenameDesktopShortcut(ExpandConstant('{userdesktop}'));
end;

// After the new files are in: the old program folder, and the old Start
// menu folder (its shortcuts point at the old folder's exe).
procedure RemoveOldNameLeftovers();
begin
  RetargetDesktopShortcut(ExpandConstant('{commondesktop}'));
  RetargetDesktopShortcut(ExpandConstant('{userdesktop}'));
  if (RenamedFromDir <> '') and not SameDir(RenamedFromDir, ExpandConstant('{app}')) then
    DeleteAppFolder(RenamedFromDir);
  DelTree(ExpandConstant('{commonprograms}\{#OldAppName}'), True, True, True);
  DelTree(ExpandConstant('{userprograms}\{#OldAppName}'), True, True, True);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    RenameFromOldName();
    CleanAppFolder();
  end
  else if CurStep = ssPostInstall then
    RemoveOldNameLeftovers();
end;

// Stop a running instance before installing or uninstalling, otherwise the
// exe is locked and the file copy fails.
procedure StopRunningApp();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'),
       '/C taskkill /F /IM "{#MyAppExeName}" >nul 2>&1',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  StopRunningApp();
  Exec(ExpandConstant('{cmd}'),
       '/C taskkill /F /IM "{#OldAppName}.exe" >nul 2>&1',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{cmd}'),
       '/C taskkill /F /IM "' + LegacyAppName + '.exe" >nul 2>&1',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := RemoveOtherCopies();
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningApp();
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    DeleteAppFolder(ExpandConstant('{app}'));
end;
