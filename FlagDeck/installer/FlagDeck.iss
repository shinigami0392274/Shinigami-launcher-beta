#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{6D1B8F52-3C4E-4C7A-9B0E-F1A6D2A4C0DE}
AppName=FlagDeck
AppVersion={#AppVersion}
AppPublisher=FlagDeck
DefaultDirName={autopf}\FlagDeck
DefaultGroupName=FlagDeck
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=FlagDeck-Setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
UninstallDisplayIcon={app}\FlagDeck.exe
CloseApplications=yes
RestartApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "startup"; Description: "Start FlagDeck with Windows (minimized to tray, needed for hotkeys)"; Flags: unchecked

[Files]
Source: "..\publish\FlagDeck.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\FlagDeck"; Filename: "{app}\FlagDeck.exe"
Name: "{autodesktop}\FlagDeck"; Filename: "{app}\FlagDeck.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FlagDeck"; ValueData: """{app}\FlagDeck.exe"" --minimized"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\FlagDeck.exe"; Description: "Launch FlagDeck"; Flags: nowait postinstall skipifsilent
