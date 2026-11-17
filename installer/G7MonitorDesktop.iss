; G7 Monitor Desktop installer (Inno Setup 6). Built by build.bat after the executable.
; Per-user install: no administrator rights, files in %LOCALAPPDATA%\Programs\G7 Monitor Desktop.

#define AppName "G7 Monitor Desktop"
#define AppExe "G7MonitorDesktop.exe"
#define AppExePath AddBackslash(SourcePath) + "..\bin\" + AppExe
#define AppVersion GetStringFileInfo(AppExePath, "ProductVersion")
#define AppMutexName "G7MonitorDesktop_App_Mutex"

[Setup]
AppId={{8C2F6E41-5A93-4D7B-B1E0-3F9D27A6C845}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Federico Castellani
AppCopyright=Copyright (C) 2026 Federico Castellani
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
MinVersion=10.0
; The app is closed (or the user is asked to) before files are replaced or removed.
AppMutex={#AppMutexName}
CloseApplications=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=G7MonitorDesktop-Setup-{#AppVersion}
SetupIconFile=..\assets\G7MonitorDesktop.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardSmallImageFile=..\assets\icon-128.png
Compression=lzma2/max
SolidCompression=yes
ShowLanguageDialog=auto

[Languages]
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "{cm:AutoStartProgram,{#AppName}}"; GroupDescription: "{cm:AutoStartProgramGroupDescription}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#AppExePath}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value the app writes for "Launch at startup", so the two settings always agree.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "G7MonitorDesktop"; ValueData: """{app}\{#AppExe}"""; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Settings and the (encrypted) device token.
Type: filesandordirs; Name: "{userappdata}\G7MonitorDesktop"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Unticking the option on an update turns autostart off (the app may have set it earlier).
  if (CurStep = ssPostInstall) and not WizardIsTaskSelected('autostart') then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'G7MonitorDesktop');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // The app can enable autostart by itself, so remove the value whoever wrote it.
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'G7MonitorDesktop');
end;
