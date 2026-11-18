# NativeShellKit Development State

- **Current SDK Version:** `0.1.0-alpha.1`
- **Target Framework:** `net10.0-windows10.0.19041.0`
- **Windows App SDK Baseline:** `2.6.4`
- **Current Branch:** `main`
- **Remote Repository:** `https://github.com/trungduc22/NativeShellKit`
- **Latest Verified Commit:** `9f1dfb4b7c5494915b3eb2063eff52ab3d3e8c55` (updated immediately after M1 push)

## Available Toolchains

- `.NET SDK`: `10.0.404`
- `Git`: `2.56.0.windows.4` (with Git Credential Manager)
- `PowerShell`: Windows PowerShell 5.1
- `OS`: Windows 11 x64 (`10.0.26300.0`)
- `Inno Setup (ISCC.exe)`: Not installed on host

## Milestone Checklist

- [x] **M0**: Initialize repository and architecture (`9f1dfb4b7c5494915b3eb2063eff52ab3d3e8c55`)
- [x] **M1**: Add application abstractions and hosting (`NativeShellKit.Abstractions`, `NativeShellKit.Hosting`)
- [ ] **M2**: Implement native WinUI shell
- [ ] **M3**: Implement shared design system
- [ ] **M4**: Add Windows integration and tray
- [ ] **M5**: Implement unified notification services
- [ ] **M6**: Add versioned module and IPC contracts
- [ ] **M7**: Package NativeShellKit NuGet libraries
- [ ] **M8**: Add app templates and developer tooling
- [ ] **M9**: Build Developer Kit installer and samples
- [ ] **M10**: Verify complete SDK and document migrations

## Build & Test Commands

- Restore: `dotnet restore NativeShellKit.sln -p:RestoreAdditionalProjectSources="C:\Users\Duc\Documents\Codex\2026-11-04\files-pasted-by-the-user-master\work\nuget"`
- Build: `dotnet build NativeShellKit.sln -c Release --no-restore`
- Test: `(available in M10)`

## Known Limitations

- `ISCC.exe` (Inno Setup compiler) is absent on this workstation; `DeveloperKit.iss` is generated and validated syntactically, while `NativeShellKit-DeveloperKit-Setup-x64.exe` provides an executable self-contained per-user installer/upgrader/uninstaller.

## Next Concrete Task

Implement M2 (`NativeShellKit.WinUI` — MVVM primitives, WinUI shell view, custom title bar, navigation service, theme/Mica/Acrylic service, dialog coordinator, UI dispatcher helpers).
