#define MyAppName "EdgeDrop"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "LegoManchik"
#define MyAppExeName "EdgeDrop.exe"
#define SourceDir "publish"

[Setup]
AppId={{C6D2B134-8A87-4B9F-8409-FD2A64539E12}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=installer_output
OutputBaseFilename=EdgeDrop_Setup_v1.0.0
SetupIconFile=EdgeDrop\icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start EdgeDrop on Windows startup"; GroupDescription: "Startup:"; Flags: unchecked
Name: "shellcontext"; Description: "Add 'Send with EdgeDrop' to Windows Explorer context menu"; GroupDescription: "File Explorer Integration:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "EdgeDrop\icon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart
Root: HKCU; Subkey: "Software\Classes\*\shell\EdgeDrop"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Send with EdgeDrop"; Flags: uninsdeletekey; Tasks: shellcontext
Root: HKCU; Subkey: "Software\Classes\*\shell\EdgeDrop"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\icon.ico"""; Flags: uninsdeletekey; Tasks: shellcontext
Root: HKCU; Subkey: "Software\Classes\*\shell\EdgeDrop"; ValueType: string; ValueName: "ExtendedSubCommandsKey"; ValueData: "EdgeDrop"; Flags: uninsdeletekey; Tasks: shellcontext
Root: HKCU; Subkey: "Software\Classes\Directory\shell\EdgeDrop"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Send with EdgeDrop"; Flags: uninsdeletekey; Tasks: shellcontext
Root: HKCU; Subkey: "Software\Classes\Directory\shell\EdgeDrop"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\icon.ico"""; Flags: uninsdeletekey; Tasks: shellcontext
Root: HKCU; Subkey: "Software\Classes\Directory\shell\EdgeDrop"; ValueType: string; ValueName: "ExtendedSubCommandsKey"; ValueData: "EdgeDrop"; Flags: uninsdeletekey; Tasks: shellcontext
Root: HKCU; Subkey: "Software\Classes\EdgeDrop\shell\OpenApp"; ValueType: string; ValueName: ""; ValueData: "Open EdgeDrop..."; Flags: uninsdeletekey; Tasks: shellcontext
Root: HKCU; Subkey: "Software\Classes\EdgeDrop\shell\OpenApp"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Open EdgeDrop..."; Flags: uninsdeletekey; Tasks: shellcontext
Root: HKCU; Subkey: "Software\Classes\EdgeDrop\shell\OpenApp"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\icon.ico"""; Flags: uninsdeletekey; Tasks: shellcontext
Root: HKCU; Subkey: "Software\Classes\EdgeDrop\shell\OpenApp\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --send-to ""select"" ""%1"""; Flags: uninsdeletekey; Tasks: shellcontext

[Run]
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""EdgeDrop Port 11000"" dir=in action=allow protocol=TCP localport=11000"; Flags: runhidden
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""EdgeDrop Discovery Port 11001"" dir=in action=allow protocol=UDP localport=11001"; Flags: runhidden
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""EdgeDrop Port 11000"""; Flags: runhidden
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""EdgeDrop Discovery Port 11001"""; Flags: runhidden
