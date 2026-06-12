; Inno Setup script for MockRoom (Windows x64).
; Compiled by packaging/windows/build.ps1, which passes:
;   /DAppVersion=<x.y.z>  /DPublishDir=<aot publish dir>  /DIconsDir=<icons dir>
; Or compile directly:  ISCC /DAppVersion=1.0.0 /DPublishDir=... /DIconsDir=... mockroom.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\src\MockRoom\bin\Release\net10.0\win-x64\publish"
#endif
#ifndef IconsDir
  #define IconsDir "..\icons"
#endif

#define AppName "MockRoom"
#define AppPublisher "MockRoom"
#define AppExe "MockRoom.exe"

[Setup]
AppId={{B3F6C1E2-7A4D-4C9B-9E21-2F8A6D5C0A11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\dist
OutputBaseFilename=MockRoom-{#AppVersion}-Setup
SetupIconFile={#IconsDir}\mockroom.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "assocfile"; Description: "Associate .mockroom files with MockRoom"; GroupDescription: "File associations:"

[Files]
; AOT publish payload — exe plus any sibling native libs (Skia/HarfBuzz). Skip debug symbols.
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#IconsDir}\mockroom.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\mockroom.ico"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\mockroom.ico"; Tasks: desktopicon

[Registry]
; .mockroom file association (created only when the assocfile task is selected).
Root: HKA; Subkey: "Software\Classes\.mockroom"; ValueType: string; ValueName: ""; ValueData: "MockRoom.Document"; Flags: uninsdeletevalue; Tasks: assocfile
Root: HKA; Subkey: "Software\Classes\MockRoom.Document"; ValueType: string; ValueName: ""; ValueData: "MockRoom Room"; Flags: uninsdeletekey; Tasks: assocfile
Root: HKA; Subkey: "Software\Classes\MockRoom.Document\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\mockroom.ico"; Tasks: assocfile
Root: HKA; Subkey: "Software\Classes\MockRoom.Document\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: assocfile

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
