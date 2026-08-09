; Mailtide Windows installer (Inno Setup 6)
; Defines expected from Nuke:
;   MyAppVersion, PublishDir, OutputDir, OutputName

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-local"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\artifacts\desktop\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts\release"
#endif
#ifndef OutputName
  #define OutputName "Mailtide-0.0.0-local-win-x64-setup"
#endif

[Setup]
AppId={{A7C3E8F1-9B2D-4E6A-8C1F-0D5B7A9E3F24}
AppName=Mailtide
AppVersion={#MyAppVersion}
AppPublisher=Skymly
DefaultDirName={localappdata}\Mailtide
DefaultGroupName=Mailtide
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Mailtide.Desktop.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Mailtide"; Filename: "{app}\Mailtide.Desktop.exe"
Name: "{autodesktop}\Mailtide"; Filename: "{app}\Mailtide.Desktop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Mailtide.Desktop.exe"; Description: "{cm:LaunchProgram,Mailtide}"; Flags: nowait postinstall skipifsilent
