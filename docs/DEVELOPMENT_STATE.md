# NativeShellKit Development State

- **Current SDK Version:** `0.1.0-alpha.1`
- **Target Framework:** `net10.0-windows10.0.19041.0`
- **Windows App SDK Baseline:** `2.6.4`
- **Current Branch:** `main`
- **Remote Repository:** `https://github.com/trungduc22/NativeShellKit`
- **Latest Verified Commit:** `b1536562083ec55fb5bfcb7ca55f90f53a4116df` (M9 pushed; M10 test suite, migration docs, versioning policy & CI verified and staged for final push)

## Available Toolchains

- `.NET SDK`: `10.0.404`
- `Git`: `2.56.0.windows.4` (with Git Credential Manager)
- `PowerShell`: Windows PowerShell 5.1
- `OS`: Windows 11 x64 (`10.0.26300.0`)
- `Inno Setup (ISCC.exe)`: Not installed on host

## Milestone Checklist

- [x] **M0**: Initialize repository and architecture (`9f1dfb4b7c5494915b3eb2063eff52ab3d3e8c55`)
- [x] **M1**: Add application abstractions and hosting (`3a4b25825a1b9e63a06dcf6f27637eeb9ef4e8fc`)
- [x] **M2**: Implement native WinUI shell (`b3b7ecaacbd49da811aa9961388b13d2dbebb4e4`)
- [x] **M3**: Implement shared design system (`1bed76ed1958e0b0e98293a2db2a9c9a9efa61f4`)
- [x] **M4**: Add Windows integration and tray (`b1bf4ed9470e6b8e4b9a0c8e27b0dbbc7889c829`)
- [x] **M5**: Implement unified notification services (`9270e815099e3c4ed2188b90ebe8d6bd527bb527`)
- [x] **M6**: Add versioned module and IPC contracts (`NativeShellKit.Security`, `NativeShellKit.Communication`, `NativeShellKit.Diagnostics`)
- [x] **M7**: Package NativeShellKit NuGet libraries (`NativeShellKit.Build`, `packaging/Pack-NuGet.ps1`, 11 `.nupkg`s `0.1.0-alpha.1` with `.xbf`/`.pri` + `SHA256SUMS.txt` + `feed-manifest.json`)
- [x] **M8**: Add app templates and developer tooling (`NativeShellKit.Templates`, `NativeShellKit.DevTools` / `nsk` CLI, `buildTransitive` `.xbf` consumer deployment)
- [x] **M9**: Build Developer Kit installer and samples (`MinimalNativeApp`, `BackgroundCompanionApp`, `UnifiedHostDemo`, `NativeShellKit-DeveloperKit-Setup-x64.exe`, `DeveloperKit.iss`, `Build-DeveloperKit.ps1`)
- [x] **M10**: Verify complete SDK and document migrations (`tests/NativeShellKit.Tests`, `VERSIONING_AND_UPGRADE_POLICY.md`, 4 app migration/compatibility docs, `.github/workflows/ci.yml`)

## Build & Test Commands

- Pack libraries & templates: `.\packaging\Pack-NuGet.ps1 -Configuration Release -Platform x64`
- Restore: `dotnet restore NativeShellKit.sln -p:RestoreAdditionalProjectSources="artifacts\packages%3B<offline-nuget-source>"`
- Build: `dotnet build NativeShellKit.sln -c Release -p:Platform=x64 --no-restore`
- Test: `dotnet test tests\NativeShellKit.Tests\NativeShellKit.Tests.csproj -c Release -p:Platform=x64 --no-build --logger "console;verbosity=normal"`
- Developer Kit: `.\packaging\Build-DeveloperKit.ps1 -Configuration Release -Platform x64`

## Known Limitations

- `ISCC.exe` (Inno Setup compiler) is absent on this workstation; `DeveloperKit.iss` is shipped alongside `NativeShellKit-DeveloperKit-Setup-x64.exe` and `Build-DeveloperKit.ps1`, which invokes `ISCC.exe` automatically when installed.
- Native WinUI toast delivery (`AppNotificationManager`) and full interactive GUI window composition are verified in headless mode / `--self-test` + `FakeTrayInterop` + `RecordingNativePresenter`; live interactive Windows desktop toast rendering requires an interactive packaged/registered user desktop session.

## Next Concrete Task

All milestones M0–M10 are complete. Maintain `0.1.0-alpha.1` verification green on every commit.
