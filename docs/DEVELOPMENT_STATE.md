# NativeShellKit Development State

- **Current SDK Version:** `0.1.0-alpha.1`
- **Target Framework:** `net10.0-windows10.0.19041.0`
- **Windows App SDK Baseline:** `2.6.4`
- **Current Branch:** `main`
- **Remote Repository:** `https://github.com/trungduc22/NativeShellKit`
- **Latest Verified Commit:** `de9a24dae19dd1b485a6dfb11f7ec56dca75b112` (M10 pushed; M11 hardening, GUI installer, interactive WinUI verification, compatibility matrix, and CI fixes staged for push)

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
- [x] **M10**: Verify complete SDK and document migrations (`de9a24dae19dd1b485a6dfb11f7ec56dca75b112`)
- [x] **M11**: Production readiness and architectural hardening (DI `Scoped` + MEDI interop, notification registration/submission status + supervised activation, WinForms GUI installer wizard + `DeveloperKit.iss` components, Light/Dark Mica & responsive UI acceptance, `PRE_MIGRATION_COMPATIBILITY_MATRIX.md`, `Build-DeveloperKit.ps1` `ISCC.exe` resolution fix, 24 xUnit tests)

## Build & Test Commands

- Pack libraries & templates: `.\packaging\Pack-NuGet.ps1 -Configuration Release -Platform x64`
- Restore: `dotnet restore NativeShellKit.sln -p:RestoreAdditionalProjectSources="artifacts\packages%3B<offline-nuget-source>"`
- Build: `dotnet build NativeShellKit.sln -c Release -p:Platform=x64 --no-restore`
- Test: `dotnet test tests\NativeShellKit.Tests\NativeShellKit.Tests.csproj -c Release -p:Platform=x64 --no-build --logger "console;verbosity=normal"`
- Developer Kit & Inno Setup: `.\packaging\Build-DeveloperKit.ps1 -Configuration Release -Platform x64`
- Interactive WinUI acceptance: `<Sample>.exe --interactive-test artifacts\screenshots\samples`
- Graphical Installer self-test: `NativeShellKit-DeveloperKit-Setup-x64.exe --gui-self-test artifacts\screenshots\installer`

## Verification & Environment Constraints

- **VERIFIED (Local Workstation):**
  - 24/24 automated xUnit tests pass (`HostingAndIdentityTests`, `SettingsAndSecurityTests`, `UiServicesAndDesignSystemTests`, `NotificationAndWindowsTests`, `CommunicationAndModulesTests`, `DiagnosticsAndPackagingTests`).
  - Interactive WinUI 3 acceptance passed with inspected screenshots (`MinimalNativeApp`, `BackgroundCompanionApp`, `UnifiedHostDemo`): Light/Dark theme + Mica backdrop switching (`dpiScale = 1.5`), custom title bar & caption contrast, `NavigationView`, `DesignSystem` controls, in-app `NotificationBannerControl`, click-to-open activation routing, and window resizing.
  - Native Windows `AppNotificationManager` registration (`Registered`), platform toast submission (`SubmittedToPlatform`), suppression (`Deduplicated`, `RateLimited`, `QuietHoursActive`, `CategoryDisabled`), error preservation (`NotRegistered`, `PlatformFailure`), and supervised async activation.
  - Graphical WinForms 5-page setup wizard (`--gui-self-test`) and CLI lifecycle (`--install`, `--upgrade`, `--verify`, `--uninstall`) with component selection and SHA-256 verification.
- **NOT TESTED / ENVIRONMENT-CONSTRAINED LOCALLY:**
  - `ISCC.exe` (Inno Setup 6 compiler) is absent on the local workstation; compiled Inno Setup verification runs on GitHub Actions `windows-latest`.
  - Desktop system tray (`Shell_TrayWnd`) taskbar icon placement returned `Win32Error=0x80004005` (`HasTaskbarShell = false`) inside the isolated agent desktop (`exebox-*`); tray menu callbacks, `HideToTray`/`ShowFromTray` events, and `IWin32Interop` calls are verified via unit tests and interactive acceptance without a live Explorer taskbar window.
  - OS toast visual banner popup appearance (`VisualDisplayConfirmed = false`) cannot be observed programmatically after `AppNotificationManager.Default.Show(...)`.

## Next Concrete Task

Monitor GitHub Actions CI run for M11 on `https://github.com/trungduc22/NativeShellKit/actions` and maintain pre-release (`0.1.0-alpha.1`) hardening.
# Local ecosystem peer grants

`LocalPeerGrant` adds an explicit per-peer DPAPI credential, independent of each app's default IPC key. Its versioned identity determines the credential location and Windows pipe name. Granting and revoking are application-owned user actions. A server must stop its existing listener before rotating or revoking a grant. The dedicated peer mode applies same-user/elevation restrictions, bounded frames and request time, single-attempt calls, an operation allowlist, and signed responses correlated to requests. Existing per-app IPC defaults are preserved.
