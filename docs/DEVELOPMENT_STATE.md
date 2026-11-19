# NativeShellKit Development State

M12 package preparation is tracked in [M12 state](migrations/M12_STATE.md). This file records the preceding M11 checkpoint.

- **Current SDK Version:** `0.2.0-alpha.1` (immutable prerelease on `convergence/sdk-2.0`)
- **Target Frameworks:** `net10.0;net10.0-windows10.0.19041.0` (`Abstractions`, `Hosting`), `net10.0` (`Build`), `net10.0-windows10.0.19041.0` (Windows/WinUI modules)
- **Windows App SDK Baseline:** `2.6.4`
- **Current Branch:** `convergence/sdk-2.0`
- **Remote Repository:** `https://github.com/trungduc22/NativeShellKit`
- **Previous Verified Baseline:** `migration/m12-sdk` (`f8f68bf`, `0.1.0-alpha.2`)

## Available Toolchains

- **GitHub Actions CI (`windows-latest`):** `.NET 10.0.x`, `ISCC.exe`, WinUI 3 GUI self-test & `.nupkg` publish gates
- **Local Workspace Environment:** Debian 12 Linux sandbox (`git`, `gh`, source authoring/inspection only; `.NET`/WinUI builds verified via GitHub Actions `windows-latest`)

## Milestone Checklist

- [x] **M0–M12**: Initial SDK 0.1.0-alpha.1 (`main`) and 0.1.0-alpha.2 (`migration/m12-sdk` `f8f68bf`) + `LocalPeerGrant` (`feature/ecosystem-integration-contracts` `7c3e34c`)
- [x] **M12.5 / Architecture 2.0 Blueprint**: Approved 7 architecture & UI/UX docs + ADR-001..012 (`docs/architecture-2.0` `76f57ea`)
- [x] **M13**: Converge `NativeShellKit` on `convergence/sdk-2.0` -> `0.2.0-alpha.1` (MEDI-backed `NativeShellHost`, `Abstractions`/`Hosting` `net10.0` multi-targeting, `ITrayService`/`IUiDispatcher`/`IStartupService`/`ISingleInstanceService`/`ILocalPeerGrant`/`LegacyIdentityOverrides`, `IShellModule` v2 topological + fault-isolated lifecycle, `buildTransitive` publish targets, single-source versioning, bounded nonce cache & 256 KiB IPC frame limit) — *Pending `windows-latest` CI verification*
- [ ] **M15**: Implement Fluent 2 UI/UX 2.0 (`NativeShellKit.DesignSystem` & `NativeShellKit.WinUI`) -> `0.2.0-alpha.2`

## Build & Test Commands

- Pack libraries & templates: `.\packaging\Pack-NuGet.ps1 -Configuration Release -Platform x64`
- Restore: `dotnet restore NativeShellKit.sln -p:RestoreAdditionalProjectSources="artifacts\packages"`
- Build: `dotnet build NativeShellKit.sln -c Release -p:Platform=x64 --no-restore`
- Test: `dotnet test tests\NativeShellKit.Tests\NativeShellKit.Tests.csproj -c Release -p:Platform=x64 --no-build --logger "console;verbosity=normal"`
- Developer Kit & Inno Setup: `.\packaging\Build-DeveloperKit.ps1 -Configuration Release -Platform x64`

## Next Concrete Task

Verify M13 `0.2.0-alpha.1` build, `.nupkg` publish `.xbf`/`.pri`, DevKit installer, and xUnit tests in `NativeShellKit CI` on `convergence/sdk-2.0`, then begin **Milestone M15** (UI/UX 2.0 `0.2.0-alpha.2`).

