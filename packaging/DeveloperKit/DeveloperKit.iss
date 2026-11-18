#ifndef AppVersion
  #define AppVersion "0.1.0-alpha.1"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\..\artifacts\developer-kit\payload"
#endif

[Setup]
AppId={{9E8B5C41-3C1A-4D77-8C12-61F4D8B91B01}
AppName=NativeShellKit Developer Kit
AppVersion={#AppVersion}
AppPublisher=trungduc22
AppPublisherURL=https://github.com/trungduc22/NativeShellKit
DefaultDirName={localappdata}\Programs\NativeShellKit\DeveloperKit
DefaultGroupName=NativeShellKit Developer Kit
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\artifacts\developer-kit\inno
OutputBaseFilename=NativeShellKit-DeveloperKit-InnoSetup-x64
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "*.dpapi,*.db,*.db-wal,*.db-shm,*.log"; Flags: ignoreversion recursesubdirs createallsubdirs

[Run]
Filename: "{app}\tools\nsk\nsk.exe"; Parameters: "doctor --feed ""{app}\packages"""; Flags: runhidden waituntilterminated; StatusMsg: "Validating NativeShellKit local NuGet feed..."

[Icons]
Name: "{autoprograms}\NativeShellKit Developer Kit Folder"; Filename: "{app}"
