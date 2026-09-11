; Inno Setup script for Tsuru.
;
; Installs per user, into %LOCALAPPDATA%\Programs\Tsuru, so no administrator
; prompt is needed. That suits a tray utility whose settings and run-at-sign-in
; entry are per-user anyway, and it avoids stacking a UAC prompt on top of the
; SmartScreen warning an unsigned download already gets.
;
; Build with:  powershell -File make-installer.ps1

#define AppName        "Tsuru"
#define AppVersion     "1.1.0"
#define AppPublisher   "Mayank Singh"
#define AppExe         "Tsuru.exe"
#define AppMutexName   "Tsuru.SingleInstance.9F2A1C"

[Setup]
; Never change AppId - it is how Windows recognises an upgrade of this app.
AppId={{8F3C1D24-9B57-4A6E-A0C2-5E7D9B4F2A11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}

PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

OutputDir=..\dist
OutputBaseFilename={#AppName}-{#AppVersion}-setup
SetupIconFile=..\build\obj\app.ico
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

; Windows 7 SP1 and later.
MinVersion=6.1sp1

; A running copy is asked to quit from [Code] instead of via AppMutex, whose
; "please close the app" prompt is suppressed during a silent run and then
; aborts the whole thing.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "startupicon"; Description: "Start {#AppName} when I sign in"; GroupDescription: "Startup:"

[Files]
Source: "..\build\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md";       DestDir: "{app}"; DestName: "README.txt"; Flags: ignoreversion isreadme

; Optional payloads - present only if the build produced them.
Source: "..\build\fonts\*";  DestDir: "{app}\fonts";  Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "..\build\assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}";  Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Written under the same value name the app itself manages, so the in-app
; "Start with Windows" checkbox stays in step with what the installer did.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"" --autostart"; \
    Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  DotNetKey = 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full';
  QuitEvent = 'Local\Tsuru.Quit.9F2A1C';
  EVENT_MODIFY_STATE = $0002;

function OpenEvent(dwDesiredAccess: DWORD; bInheritHandle: BOOL; lpName: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: THandle): BOOL;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

{ Asks a running copy to shut down.

  The app is signalled rather than killed so it removes its own tray icon and
  saves state; Windows leaves a phantom icon behind when a tray app is
  terminated. taskkill is only a backstop for a copy too wedged to answer. }
procedure StopRunningApp;
var
  Handle: THandle;
  ResultCode: Integer;
begin
  Handle := OpenEvent(EVENT_MODIFY_STATE, False, QuitEvent);
  if Handle <> 0 then
  begin
    SetEvent(Handle);
    CloseHandle(Handle);
    Sleep(1800);
  end;

  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExe} /F', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(400);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp;
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningApp;
  Result := True;
end;

{ The executable targets .NET Framework 4.x, which ships with Windows 8 and
  later but may be absent on a bare Windows 7. }
function InitializeSetup(): Boolean;
var
  Release: Cardinal;
begin
  Result := True;
  if RegQueryDWordValue(HKLM, DotNetKey, 'Release', Release) then
    Exit;
  if RegKeyExists(HKLM, DotNetKey) then
    Exit;

  { Suppressible so an unattended install proceeds instead of blocking on a
    dialog nobody is there to answer. }
  Result := SuppressibleMsgBox(
    '{#AppName} needs the .NET Framework 4, which does not appear to be installed.' + #13#10#13#10 +
    'You can install it from microsoft.com and then run this setup again.' + #13#10#13#10 +
    'Continue anyway?', mbConfirmation, MB_YESNO, IDYES) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ConfigDir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  { The app may have written this itself after install, in which case the
    installer's own uninsdeletevalue never registered it. Remove it either way
    so uninstalling never leaves a startup entry pointing at a deleted file. }
  RegDeleteValue(HKCU, RunKey, '{#AppName}');

  ConfigDir := ExpandConstant('{userappdata}\{#AppName}');
  if not DirExists(ConfigDir) then
    Exit;

  { Settings are the user's, so removing them is offered rather than assumed.
    A silent uninstall defaults to keeping them: deleting someone's data with
    no one watching is the worse mistake of the two. }
  if SuppressibleMsgBox('Remove your {#AppName} settings as well?' + #13#10#13#10 + ConfigDir,
                        mbConfirmation, MB_YESNO, IDNO) = IDYES then
    DelTree(ConfigDir, True, True, True);
end;
