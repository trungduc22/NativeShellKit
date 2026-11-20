# M12 migration state

## Audited baselines

| Repository | Baseline | Framework and findings | M12 status |
| --- | --- | --- | --- |
| NativeShellKit | `89147f1058f853eb423624d468780aab1e064e65` | .NET 10, Windows App SDK 2.6.4, M11 CI 44350287513 VERIFIED. M11 added scoped DI, Microsoft DI interop and new notification outcome contracts under the old package version. | Package train advances to 0.1.0-alpha.2 on `migration/m12-sdk`. |
| HiberDesk | `54307e79d9fd1eff60cdd4c193588389e9e221c3` + `9bee11e34d599156d232e960b66eb43b49afa08d` | .NET 10 / WinAppSDK 2.6.5. The verified branch descends directly from main, includes organizer collision fixes and more isolated integration fixtures, and passed CI 44275505753. | First pilot uses the verified branch as its base. |
| GhostCode | `4bc43e284701a25171438f342261bcd6cc484d3b` | .NET 10 / WinAppSDK 2.6.5. The release pipeline passed at this SHA. The archive and cloud credential stores are domain-owned. | Migration NOT TESTED pending the HiberDesk gate. |
| ED-Local-AI | `e0dfea3aa7d89e2782c8b37ed8ff13f081bfcca7` | Two .NET 8 UI projects reference WinAppSDK 1.8.260921001. Latest successful workflow checked the preceding code commit `c385f2b`. | Migration NOT TESTED pending HiberDesk and GhostCode validation. |
| BackThread | `4710def` inspected checkpoint | .NET 8 headless Host, WinForms Tray and iOS components. A separate SDK compatibility experiment exists locally. | Excluded from M12 changes. |

## Compatibility decisions

- Preserve assembly version 0.1.0.0 while advancing the SDK package/informational version to 0.1.0-alpha.2 and file version to 0.1.0.2. Existing alpha.1 payloads stay immutable. Package only into an empty directory.
- Keep the SDK's existing Windows App SDK floor for this package train. HiberDesk/GhostCode retain their direct 2.6.5 references and keep their .NET 10 platform boundary. Verify actual transitive resolution from package consumers.
- The M11 compatibility matrix correctly calls out Microsoft DI Abstractions as a real dependency of Abstractions despite the zero-dependency contributor guideline. Treat the existing bridge as a versioned contract, rather than silently removing it during packing.
- Keep compatibility at application composition roots. NativeShellKit's data directory, DPAPI entropy, mutex and pipe conventions are distinct from existing app conventions. Use a distinct SDK metadata namespace. Continue routing existing application settings, encrypted secrets and browser messaging through the original implementations during the pilot.

| Persistent contract | HiberDesk | GhostCode | ED-Local-AI |
| --- | --- | --- | --- |
| Data | `%LOCALAPPDATA%\HiberDesk`, config.json plus configured SQLite path. Preserve startup, restore and sync settings. | Existing configured archive root, catalog and SQLite files. | WinUI uses `%LOCALAPPDATA%\EDLocalAI\data`. Practice Fast uses `%LOCALAPPDATA%\EDLocalAI-PracticeFast\data` and imports selected missing legacy files. |
| Secrets | Existing HiberDesk DPAPI store under `secrets`, with current formats and entropy. | `GhostCode.OAuth.DPAPI.v1` and `GhostCode.OAuthClientSecret.DPAPI.v1`. | `native-auth.dpapi`, DPAPI CurrentUser with null entropy. |
| Native integration | `com.hiberdesk.nativehost`, three existing pipe/mutex names, Run value `HiberDesk`, existing installer AppId. | `com.ghostcode.bridge`, current installer AppId and archive/activation behavior. | Existing variant behavior and program mutexes. Keep each variant executable. |
| Major risk | Startup and browser bridge side effects during GUI testing. Use documented isolated roots. | Archive deletion/upgrade and secret compatibility. Use copied fixtures only. | WinUI and runtime replatforming across distinct roots with a one-way first-run import. Test both separately. |

The alpha.2 source retains M11 IPC behavior; the local BackThread IPC protocol-2 experiment is not in this package train. Do not switch existing application-specific IPC to this SDK IPC as part of the pilot. Use independent clean restore/build and verify package manifests and XAML resources before advancing consumers.

Rollback uses the original branches and previously built binaries with the same existing app data. This branch publishes development commits and CI evidence, not public NuGet/installer releases.

## Initial verification

VERIFIED locally with .NET SDK 10.0.404. The Release x64 pack produced 13 packages. All package SHA-256 values matched the feed manifest, and the WinUI and DesignSystem archives contained XBF and PRI resources. An independent copy of MinimalNativeApp restored from the mapped local feed and nuget.org, built with zero warnings/errors, and passed its executable self-test. All seven resolved NativeShellKit assemblies matched the package payload hashes. The sample needed explicit implicit usings and nullable settings outside the SDK repository. Windows interactive and app-specific verification are separate gates.

## Pilot checkpoint

| Gate | Status and reference |
| --- | --- |
| NativeShellKit packages | VERIFIED. Alpha.2 pinned source `2a73d5f5d3772d0943ed362f18bd45ecaf8278ae` passed CI 44440917801 and the independent Release consumer. |
| HiberDesk | VERIFIED for shared host, tray, WinUI, in-app notifications and submission to Windows. `migration/nativeshellkit-hiberdesk` app source `918e43a`, CI 44443430005. The installation validation checkpoint is `21d6ca3`. |
| GhostCode | Baseline release CI VERIFIED at `4bc43e2`. Audit checkpoint `c39d243` on `migration/nativeshellkit-ghostcode`. Application migration is pending the pilot gate. |
| ED-Local-AI | Baseline regression VERIFIED, 235/235 assertions for each variant on .NET 8.0.34. Audit checkpoint `367cb22` on `migration/nativeshellkit-ed-local-ai`. Moving either variant to the SDK is NOT TESTED. |
| Cross-app install, notifications, activation and module readiness | NOT TESTED. Requires a second application with the same verified SDK. |
| BackThread | Excluded from M12. Keep the existing Windows and iOS/audio branches. |

The HiberDesk worktree keeps startup and IPC identifiers, SQLite, app settings and domain secrets. It has driven live Edge and Thorium at the tray checkpoint and built the current portable and Inno artifacts. The app's own migration state records the current browser rerun, live notification evidence, performance and upgrade checklist. Windows toast submission and actual toast display are tracked separately.
