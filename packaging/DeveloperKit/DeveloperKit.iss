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
DisableDirPage=no
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UninstallDisplayName=NativeShellKit Developer Kit

[Types]
Name: "full"; Description: "Full Developer Kit (NuGet Feed, CLI, Templates, Samples & Docs)"
Name: "compact"; Description: "Compact Installation (NuGet Feed, CLI & Project Templates)"
Name: "custom"; Description: "Custom Installation"; Flags: iscustom

[Components]
Name: "packages"; Description: "Local NuGet Package Feed (NativeShellKit.*.nupkg & checksums)"; Types: full compact custom; Flags: fixed
Name: "cli_templates"; Description: "Developer CLI (nsk.exe) & dotnet new Project Templates"; Types: full compact custom
Name: "samples"; Description: "Reference WinUI 3 Sample Applications"; Types: full custom
Name: "docs"; Description: "Architecture, Versioning & App Migration Guides"; Types: full custom

[Files]
Source: "{#PayloadDir}\packages\*"; DestDir: "{app}\packages"; Components: packages; Excludes: "*.dpapi,*.db,*.db-wal,*.db-shm,*.log"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\tools\*"; DestDir: "{app}\tools"; Components: cli_templates; Excludes: "*.dpapi,*.db,*.db-wal,*.db-shm,*.log"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\templates\*"; DestDir: "{app}\templates"; Components: cli_templates; Excludes: "*.dpapi,*.db,*.db-wal,*.db-shm,*.log"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\samples\*"; DestDir: "{app}\samples"; Components: samples; Excludes: "*.dpapi,*.db,*.db-wal,*.db-shm,*.log"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\docs\*"; DestDir: "{app}\docs"; Components: docs; Flags: ignoreversion recursesubdirs createallsubdirs

[Run]
Filename: "{app}\tools\nsk\nsk.exe"; Parameters: "doctor --feed ""{app}\packages"""; Components: cli_templates; Flags: runhidden waituntilterminated; StatusMsg: "Validating NativeShellKit local NuGet feed..."

[Icons]
Name: "{autoprograms}\NativeShellKit Developer Kit Folder"; Filename: "{app}"

[UninstallDelete]
Type: files; Name: "{app}\install-manifest.json"
