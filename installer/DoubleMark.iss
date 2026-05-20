#define MyAppName "DoubleMark"
#define MyAppVersion "2.1.0"
#define MyAppPublisher "DoubleMark"
#define MyAppExeName "DoubleMark.exe"
#define SourceDir "..\dist\DoubleMark"

[Setup]
AppId={{B6FD1581-5272-489E-B177-51765F27C534}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\DoubleMark
DefaultGroupName=DoubleMark
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=DoubleMarkSetup-{#MyAppVersion}
SetupIconFile=..\src\DoubleMark.Desktop\Assets\Branding\doublemark-logo.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\DoubleMark"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\DoubleMark"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,DoubleMark}"; Flags: nowait postinstall skipifsilent
