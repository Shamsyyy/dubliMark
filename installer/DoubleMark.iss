#ifndef MyAppBuildId
#define MyAppBuildId ""
#endif

#define MyAppName "DoubleMark"
#define MyAppVersion "2.1.5"
#define MyAppPublisher "DoubleMark"
#define MyAppCopyright "Copyright (C) DoubleMark"
#define MyAppExeName "DoubleMark.exe"
#define SourceDir "..\dist\DoubleMark"

[Setup]
AppId={{B6FD1581-5272-489E-B177-51765F27C534}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright={#MyAppCopyright}
AppSupportURL=https://doublemark.ru/
AppPublisherURL=https://doublemark.ru/
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=DoubleMark - Chestny ZNAK scan and print
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCopyright={#MyAppCopyright}
AppMutex=DoubleMarkAppRunning
CloseApplications=force
CloseApplicationsFilter=DoubleMark.exe,DoubleMark.Desktop.exe
RestartApplications=no
DefaultDirName={autopf}\DoubleMark
DefaultGroupName=DoubleMark
DisableProgramGroupPage=yes
OutputDir=..\dist\installer
OutputBaseFilename=DoubleMarkSetup-{#MyAppVersion}{#MyAppBuildId}
SetupIconFile=..\src\DoubleMark.Desktop\Assets\Branding\doublemark-logo.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.download,*.tmp,*.log,*.tests.exe,*.Test*.exe"

[Icons]
Name: "{group}\DoubleMark"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\DoubleMark"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,DoubleMark}"; Flags: nowait postinstall skipifsilent
