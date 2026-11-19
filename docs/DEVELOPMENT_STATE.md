# NativeShellKit Development State

M12 package preparation is tracked in [M12 state](migrations/M12_STATE.md). This file records the SDK 2.0 convergence milestones.

- **Current SDK Version:** `0.2.0-alpha.3` (immutable prerelease on `convergence/sdk-2.0`)
- **Target Frameworks:** `net10.0;net10.0-windows10.0.19041.0` (`Abstractions`, `Hosting`), `net10.0` (`Build`), `net10.0-windows10.0.19041.0` (Windows/WinUI modules)
- **Windows App SDK Baseline:** `2.6.4`
- **Current Branch:** `convergence/sdk-2.0`
- **Remote Repository:** `https://github.com/trungduc22/NativeShellKit`
- **Previous Verified Baseline:** `migration/m12-sdk` (`f8f68bf`, `0.1.0-alpha.2`), M13 `convergence/sdk-2.0` (`b7fb514`, `0.2.0-alpha.1`, CI Run `44454928063`), M15 `convergence/sdk-2.0` (`b14126e`, `0.2.0-alpha.2`, CI Run `44459215095`)

## Available Toolchains

- **GitHub Actions CI (`windows-latest`):** `.NET 10.0.x`, `ISCC.exe`, WinUI 3 GUI self-test, interactive screenshot acceptance, & `.nupkg` publish gates
- **Local Workspace Environment:** Debian 12 Linux sandbox (`git`, `gh`, source authoring/inspection only; `.NET`/WinUI builds verified via GitHub Actions `windows-latest`)

## Milestone Checklist

- [x] **M0–M12**: Initial SDK 0.1.0-alpha.1 (`main`) and 0.1.0-alpha.2 (`migration/m12-sdk` `f8f68bf`) + `LocalPeerGrant` (`feature/ecosystem-integration-contracts` `7c3e34c`)
- [x] **M12.5 / Architecture 2.0 Blueprint**: Approved 7 architecture & UI/UX docs + ADR-001..012 (`docs/architecture-2.0` `76f57ea`)
- [x] **M13**: Converge `NativeShellKit` on `convergence/sdk-2.0` -> `0.2.0-alpha.1` (VERIFIED in `NativeShellKit CI` Run `44454928063`, `b7fb514`)
- [x] **M15**: Implement Fluent 2 UI/UX 2.0 (`NativeShellKit.DesignSystem` & `NativeShellKit.WinUI`, `NativeShellFrame`, `ShellTitleBar`, templated `Generic.xaml` controls, migrated samples & template, expanded xUnit suite) -> `0.2.0-alpha.3` (visual polish on `StatusBadge` & responsive `ShellTitleBar` from `0.2.0-alpha.2` Run `44459215095` screenshot inspection)
- [ ] **M14 & M16**: Converge `HiberDesk` (`convergence/hiberdesk-2.0`) onto `NativeShellKit 0.2.0-alpha.3` & UI 2.0
- [ ] **M17**: Converge `GhostCode` (`convergence/ghostcode-2.0`) onto `NativeShellKit 0.2.0-alpha.3` & UI 2.0

## Build & Test Commands

- Pack libraries & templates: `.\packaging\Pack-NuGet.ps1 -Configuration Release -Platform x64`
- Restore: `dotnet restore NativeShellKit.sln -p:RestoreAdditionalProjectSources="artifacts\packages"`
- Build: `dotnet build NativeShellKit.sln -c Release -p:Platform=x64 --no-restore`
- Test: `dotnet test tests\NativeShellKit.Tests\NativeShellKit.Tests.csproj -c Release -p:Platform=x64 --no-build --logger "console;verbosity=normal"`
- Developer Kit & Inno Setup: `.\packaging\Build-DeveloperKit.ps1 -Configuration Release -Platform x64`

## Next Concrete Task

Verify `0.2.0-alpha.3` CI run + updated screenshots, pin commit SHA for consumers, then execute **M14 & M16** on `HiberDesk` (`convergence/hiberdesk-2.0`).
