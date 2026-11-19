# NativeShellKit 2.0 — Ecosystem Migration Roadmap & Verification Plan

**Status:** Approved Implementation Roadmap (`docs/architecture-2.0`)  
**Primary Execution Model:** Autonomous Gemini Primary Engineer in small, independently testable milestones (no routine dependency on Opus/Sonnet; escalate only major architectural tradeoffs, security boundaries, high-risk migrations, or unresolved concurrency failures via `REVIEW_REQUEST.md`).

---

## 1. Non-Negotiable Compatibility & Data-Preservation Charter

Every migration milestone MUST pass automated byte/hash preservation checks on existing user data:

1. **Zero Data Path Mutation:** Existing SQLite DBs, `config.json`, DPAPI secret files, encryption entropies, browser Native Messaging manifests, Startup Run values, and Inno Setup `AppId`s stay untouched (see exact inventory in `docs/architecture/BRANCH_CONVERGENCE.md` §5).
2. **SDK Metadata Isolation:** NativeShellKit's own logs, notification history, and peer grants live inside `<appDataRoot>\trungduc22\<appId>\` — never overwriting product files.
3. **Unpackaged Self-Contained Independence:** End-user machines NEVER require the NativeShellKit Developer Kit installed.
4. **Upgrade & Rollback Parity:** Every installer milestone must prove 10-step clean-VM install -> upgrade -> config/SQLite SHA-256 verification -> rollback -> uninstall.

---

## 2. HiberDesk Migration & Optimization Strategy

**Base Branch:** `origin/migration/nativeshellkit-hiberdesk` (`495e027`, already containing PR #1 `9bee11e` and M12.2–M12.7).  
**Target Branch:** `convergence/hiberdesk-2.0`.

### Stage H1 — Single-Host Convergence (`NativeShellKit 0.2.0-alpha.1`)
1. Upgrade all `NativeShellKit.*` NuGet packages in `HiberDesk.App.csproj` from `[0.1.0-alpha.2]` to `[0.2.0-alpha.1]` and add `NativeShellKit.Communication` `[0.2.0-alpha.1]`. **Never** add `.gitmodules` or `vendor/NativeShellKit`.
2. Port `src/HiberDesk.Integrations.BackThread` (pure `net10.0`) + `BackThreadBridgeTests.cs` verbatim from `d196a9f`.
3. Eliminate `EcosystemRuntime`'s duplicate `NativeShellHost` and duplicate `UnifiedNotificationService`:
   - Register `EcosystemSettingsStore`, `BackThreadConnection`, `GhostCodeConnection`, and `CompanionRelayService` on HiberDesk's **single canonical `NativeShellHost`**.
   - Register `BackThreadIntegrationModule` (`StartupOrder = 100, IsCritical = false`) and `GhostCodeIntegrationModule` (`StartupOrder = 110, IsCritical = false`).
   - Subscribe `CompanionRelayService` directly to the canonical `INotificationService.InAppNotificationRaised` (removing duplicate `PublishAsync` calls from `MainViewModel.cs` and replacing `_seen` 256-cap drop bug with a 512-entry LRU+TTL ring buffer).
4. Register `NativeTrayService` (`ITrayService`), `IUiDispatcher`, and `LegacyIdentityOverrides` (preserving `HiberDesk` Startup Run value, `--background`, `Local\HiberDesk_SingleInstance_Mutex_v1`, `HiberDesk_SingleInstance_Pipe_v1`) via DI.
5. Port `CompanionViewModel`, `CompanionPanel`, `OverviewPage` ecosystem card, `DevicesPage` companion section, `HiberDesk.iss` optional `#ifdef CompanionHostDir` task, and `UiSelfTestRunner` companion checks — preserving M12 `{ThemeResource}` styling (zero `HiberThemePalette` regression).

### Stage H2 — Native UI/UX 2.0 Adoption (`NativeShellKit 0.2.0-alpha.2`)
1. Replace manual `MainWindow.xaml` chrome (`CustomTitleBarGrid` + `UpdateTitleBarRegions` + `RootNav` + `SdkBanner`) with `NativeShellFrame` (`Mode=ModuleHost`, `TrayFirst`) + `ShellTitleBar` (auto passthrough for `GlobalSearchBox`!).
2. Refactor `CompanionPanel` to use `CompactMediaBar` + `ModuleCardBoundary` + `SettingsRow` (expandable) + `DialogCoordinator` 2.0 (replacing manual setup `ContentDialog`).
3. Replace ad-hoc status pills and loading/empty indicators across the 7 pages with `StatusBadge`, `PageHeader`, `MetricCard`/`MetricStrip`, `StateView`, and `ProgressBanner`.
4. Verify Light, Dark, HighContrast, 4 window widths (`600px`, `900px`, `1280px`, `1600px`), and `150%` DPI via `UiSelfTestRunner`.

### Stage H3 — Performance & Reliability Refactoring
1. **`SqliteWorkspaceStore`:** Run WAL/synchronous PRAGMAs once in `InitializeAsync`; enable connection pooling; add single-transaction `GetDashboardSnapshotAsync()`.
2. **`MainViewModel`:** Debounce `protocol.StateUpdated` (250ms single-flight); move `DiscoverInstalledBrowsers` off UI thread with 30s TTL cache; adopt `ObservableCollectionSync.ApplyKeyedDiff` + domain-scoped refresh (`RefreshBrowserStateAsync`, `RefreshFilesAsync`, etc.).
3. **`SafeFileOrganizer`:** Increase `FileSystemWatcher.InternalBufferSize` to `65536`; wire `watcher.Error` overflow recovery scan; replace unbounded `Task.Run` per event with bounded `ConcurrentDictionary` + 350ms coalescing loop registered on `IBackgroundTaskSupervisor`.
4. **Workers & Leaks:** Register `BackThreadConnection` and `GhostCodeConnection` loops on `IBackgroundTaskSupervisor`; cache `IpcAuthManager` inside `LocalPeerGrant`; enable cached primary routes in `NativeShellFrame` and verify zero leaked handlers in 100-navigation soak test.

---

## 3. GhostCode Migration Strategy

**Base Branch:** `origin/migration/nativeshellkit-ghostcode` (`fa44d1a`, M12.2 verified 36/36 GUI checks).  
**Target Branch:** `convergence/ghostcode-2.0`.

### Stage G1 — Single-Host Peer Convergence (`NativeShellKit 0.2.0-alpha.1`)
1. Upgrade `NativeShellKit.Hosting` & `NativeShellKit.Diagnostics` to `[0.2.0-alpha.1]` and add `NativeShellKit.Communication` `[0.2.0-alpha.1]`. **Reject** `.gitmodules` and `vendor/NativeShellKit` from `a19742e`.
2. Port `NotificationCoordinator.NotificationEligible` and `HiberDeskPeer` (as DI-managed `HiberDeskPeerModule : IShellModule` using `IUiDispatcher` and `LocalPeerGrant` from NuGet — removing static `App.CompanionPeer`).
3. Port `SettingsPage` bilingual En/Vi opt-in grant/revoke control.
4. Keep `GhostCode.OAuth.DPAPI.v1`, `GhostCode.OAuthClientSecret.DPAPI.v1`, `com.ghostcode.bridge`, SQLite archive store, WAL trash journal, and Inno `AppId={{8F9C31E2-7C2A-4D4B-9D8F-2B9E6C41A010}` 100% untouched.

### Stage G2 — Shared Tray, Notifications & UI/UX 2.0 (`NativeShellKit 0.2.0-alpha.2`)
1. Adopt `NativeShellKit.Tray` (`ITrayService`), `NativeShellKit.Notifications` (bridging `NotificationCoordinator` dedup/eligibility onto `INotificationService`), `NativeShellKit.WinUI` (`NativeShellFrame(Mode=Dashboard, TrayFirst)`), and `NativeShellKit.DesignSystem` (retiring `GhostThemePalette` while preserving GhostCode's 9-state cloud sync `StatusBadge` and full-width GitHub-style `NskMonoCodeTextStyle` diff viewer).
2. Apply `ObservableCollectionSync.ApplyKeyedDiff` to high-churn Archive/Diff/UploadQueue collections and eliminate `.GetAwaiter().GetResult()` in `ToolchainDiscoveryService.cs:143`.
3. Run dual-app coexistence & E2E peer test: HiberDesk + GhostCode running simultaneously under same Windows user -> grant -> live status + archive event relay -> open archive activation -> revoke -> unauthorized rejection -> re-grant rotation.

---

## 4. BackThread Strict Isolation Requirements

BackThread **remains 100% independent** and MUST NOT depend on `NativeShellKit.*`, `Microsoft.WindowsAppSDK`, or WinUI 3:

1. **Domain Ownership:** BackThread exclusively owns USB/usbmuxd transport, iOS native app & BTH1 wire protocol, WASAPI loopback & real-time PCM audio streaming, Hybrid MediaRouter, Spotify PKCE, `BackThread.Host.Core` (`codex/native-foundation`), and standalone CLI/WinForms Tray host.
2. **Contract-Only Integration Boundary:** Communication with HiberDesk is strictly limited to `docs/HIBERDESK_BRIDGE.md`:
   - Opt-in listener (`enableHiberDeskBridge: true` or `--hiberdesk` flag).
   - Same-user named pipe `BackThread.HiberDesk.v1.<WindowsSID>.<ApiPort>` with `X-BackThread-Bridge: 1`.
   - HMAC-SHA256 derived bearer (`BackThread:local-api:hiberdesk:v1`); raw `session.key` rejected on pipe; derived bearer rejected on TCP.
   - Strict HTTP route allowlist (`HiberDeskApi.Allows`), `pauseWindowsOnConfirmedHandoff` host veto, `HostInstanceLease` file lock (`%LOCALAPPDATA%\BackThread\runtime\{usb|localhost-<port>}.lock`), and `OwnsWindowsNotification` dedup skip for `HiberDesk`/`GhostCode`.
3. **CI & Branch Independence:** Port `integration/hiberdesk-bridge` (`63a0abf`, `1d6008e`) onto BackThread's stabilized Windows branch with `Windows regression` CI enabled; verify zero regression to iPhone audio soak tests or standalone TCP API.

---

## 5. ED-Local-AI Future Compatibility & Migration Plan (Zero Edits Now)

**Active Parallel Session Rule:** Another agent is actively performing bug fixes on `ed-local-ai`. **DO NOT edit, commit, rebase, or merge `ed-local-ai` during M13–M17.** ED-Local-AI MUST NOT block NativeShellKit 2.0, HiberDesk, or GhostCode convergence.

### 5.1 Current Baseline (Read-Only Audit of `origin/main` `e0dfea3`)
- **Two Independent Portable Variants:**
  - Main WinUI (`winui3/EDLocalAI.csproj`): `AssemblyName=ED-Local-AI`, `Version=0.7.4`, data `%LOCALAPPDATA%\EDLocalAI\data`, mutex `EDLocalAI.Native.<user>`.
  - Practice Fast (`practice-fast/EDLocalAI.csproj`): `AssemblyName=ED-Local-AI-Practice-Fast`, `Version=0.7.5`, data `%LOCALAPPDATA%\EDLocalAI-PracticeFast\data`, mutex `EDLocalAI.PracticeFast.<user>`, one-way first-run import of missing `native-settings.json`, `native-auth.dpapi`, `native-answer-memory.json`, and `progress/*.json` from `%LOCALAPPDATA%\EDLocalAI\data`.
- **Current Platform:** `net8.0-windows10.0.19041.0`, `TargetPlatformMinVersion=10.0.17763.0`, `Microsoft.WindowsAppSDK 1.8.260921001`, `ProtectedData 8.0.0`, `PublishSingleFile=true` + `EnableCompressionInSingleFile=true`, `EnableDefaultItems=false`, `Nullable=disable`, pure C#-constructed WinUI UI (`winui3/src/App.cs`, `practice-fast/src/App.cs`).

### 5.2 Entry Prerequisites Before Starting ED-Local-AI Migration (M18)
1. Active ED-Local-AI bug-fix session completes and merges to `ed-local-ai/main` with green `Windows builds` CI and 235/235 assertions passing for both variants.
2. NativeShellKit `0.2.0-beta.1` is verified across BOTH HiberDesk and GhostCode (including `CompactUtility` shell mode & `DensityCompact.xaml` verified in a dedicated sample app).
3. `PublishSingleFile=true` compatibility spike passes: confirm WinUI 3 `.xbf`/`.pri` resolution with `NativeShellKit.WinUI` + `NativeShellKit.DesignSystem` under `PublishSingleFile=true` + `WindowsAppSDKSelfContained=true` (or document side-by-side portable ZIP fallback if WinAppSDK 2.6.x single-file XBF extraction has platform restrictions).

### 5.3 Preservation & Coexistence Guarantees
- **Immutable Data & Secrets:** Retain `%LOCALAPPDATA%\EDLocalAI\data` and `%LOCALAPPDATA%\EDLocalAI-PracticeFast\data`, exact `native-settings.json` schema, `native-answer-memory.json`, `progress/*.json`, `winui-run-report.json`, and **`native-auth.dpapi` (DPAPI `CurrentUser`, `null` entropy)** — NEVER force migration to NativeShellKit's non-null entropy `DpapiSecretStore`.
- **First-Run Import Logic:** Keep Practice Fast's one-way non-overwriting import from `EDLocalAI\data` intact and covered by automated regression tests.
- **Two-Variant Coexistence:** Use distinct `AppIdentity` slugs (`ed-local-ai` vs `ed-local-ai-practice-fast`) paired with `LegacyIdentityOverrides` retaining exact mutexes (`EDLocalAI.Native.<user>` and `EDLocalAI.PracticeFast.<user>`) so both variants can be installed and executed side-by-side without mutex or metadata collision.

### 5.4 Incremental XAML Migration Boundary (Keeping Compact Workflow)
1. **Phase E1 (Runtime Upgrade Only — Zero UI Rewrite):** Upgrade both `.csproj`s from `net8.0` / `WinAppSDK 1.8` -> `net10.0` / `WinAppSDK >= 2.6.5` on a dedicated branch; pass 235/235 backend/fixture checks + `--self-test` + `--navigation-smoke` for BOTH variants.
2. **Phase E2 (Host & Compact Shell Adoption):** Adopt `NativeShellKit.Hosting` + `NativeShellKit.WinUI` (`NativeShellFrame(Mode=CompactUtility, Density=Compact)`) + `NativeShellKit.Diagnostics` (moving `Log()`'s sync `File.AppendAllText` off UI thread onto `RedactingFileLogger`). Keep `Engine`, `Navigator`, `BrowserClient`, `Cdp`, `ModelClient`, `ProgressStore`, and `Settings` **untouched**.
3. **Phase E3 (Incremental XAML View Extraction):** Replace procedural C# helpers (`Card()`, `Rows()`, `Field()`, `Btns()`, `ProgressPanel()`) one tab at a time with XAML views (`ControlTabView.xaml`, `ProgressTabView.xaml`, `LogsTabView.xaml`) using `DesignSystem` compact tokens (`NskSurfaceCardBrush`, `StateView`, `StatusBadge`, `ProgressBanner`), preserving the exact 3-tab compact workflow and Vietnamese copy.
4. **Conflict-Avoidance Protocol:** If urgent ED-Local-AI bug fixes land on `main` during E1–E3, keep all `Engine`/`Navigator`/`Cdp`/`Coordinator` files unmodified by the UI migration branch so rebasing onto `main` stays conflict-free.

---

## 6. Sequenced Implementation Milestones & Testable Acceptance Criteria

| Milestone | Repository | Deliverable & Package Version | Testable Acceptance Criteria |
| --- | --- | --- | --- |
| **M13: SDK Convergence & Canonical Host** | `NativeShellKit` (`convergence/sdk-2.0`) | Merge `m12-sdk` + `ecosystem-integration-contracts` + `docs/architecture-2.0`; MEDI-backed `NativeShellHost`; multi-target `Abstractions`/`Hosting` (`net10.0;net10.0-windows`); add `ITrayService`, `IUiDispatcher`, `IStartupService`, `ISingleInstanceService`, `LegacyIdentityOverrides`, `IShellModule` v2, nonce replay cache; single-source versioning; fixed `buildTransitive` publish targets; pack **`0.2.0-alpha.1`**. | 1. 13 immutable `0.2.0-alpha.1` `.nupkg`s + SHA-256 manifest.<br>2. All xUnit tests pass + new tests for MEDI scope validation, non-deadlocking factory resolution, module fault isolation, `LocalPeerGrant` nonce replay rejection, and `net10.0` pure-core compilation.<br>3. Clean external `dotnet publish` contains all `.xbf`/`.pri` assets.<br>4. `NativeShellKit CI` green. |
| **M14: HiberDesk Single-Host Convergence** | `HiberDesk` (`convergence/hiberdesk-2.0`) | Base on `495e027`; upgrade to `0.2.0-alpha.1` NuGet; selectively port BackThread & GhostCode hub from `d196a9f` with **zero submodules**, **one `NativeShellHost`**, **one `INotificationService`**, supervised workers, and LRU+TTL relay dedup. | 1. Zero `.gitmodules` / `vendor/` references.<br>2. Unit test asserts single `IAppHost` & single `INotificationService` instance.<br>3. Core (21/21) + Integration + `BackThreadBridgeTests` + real Edge/Thorium tests pass.<br>4. Isolated WinUI self-test passes (banner + companion panel).<br>5. 10-step Inno install/upgrade/rollback preserves config & SQLite SHA-256. |
| **M15: Native UI/UX 2.0 & Shell Variants** | `NativeShellKit` (`convergence/sdk-2.0`) | Implement Fluent 2 token dictionaries (`Light`/`Dark`/`HighContrast` + `Compact`/`Standard` density), `NativeShellFrame` (5 modes), `ShellTitleBar` (auto passthrough), 100% DP-backed templated controls, `CompactMediaBar`, `ProgressBanner`, `ModuleCardBoundary`, `DialogCoordinator` 2.0, `ObservableCollectionSync`; pack **`0.2.0-alpha.2`**. | 1. Zero `Color.FromArgb` in `DesignSystem` C# controls.<br>2. Automated unit tests for DP binding, contrast ratio `>= 4.5:1`, reduced motion, `ObservableCollectionSync` keyed diffing.<br>3. Sample apps (`Minimal`, `Companion`, `UnifiedHost` + new `CompactUtility` verification harness) pass Light/Dark/HighContrast/DPI screenshots.<br>4. `Generic.xbf` + `DesignSystemThemeResources.xbf` verified in `.nupkg` and clean consumer `dotnet publish`. |
| **M16: HiberDesk UI 2.0 & Performance** | `HiberDesk` (`convergence/hiberdesk-2.0`) | Upgrade to `0.2.0-alpha.2`; adopt `NativeShellFrame(ModuleHost, TrayFirst)`, `CompactMediaBar`, `ModuleCardBoundary`, 2.0 DesignSystem controls; implement SQLite WAL pooling + batched snapshot, debounced `RefreshAllAsync` + keyed diffing, bounded 64 KiB `SafeFileOrganizer` coalescer, cached primary pages. | 1. All 9 performance budgets in `PLATFORM_2_0.md` §5.2 verified green on Release x64 (cold TTF <= 650ms, 50-burst <= 4 refreshes / <= 16ms stall, 500-file FSW burst = 1 worker & 0 drops, <= 6 supervised workers, 100-nav soak = 0 leaked handlers, shutdown <= 1.5s).<br>2. Interactive WinUI self-test passes Light/Dark/HighContrast across 4 widths.<br>3. Inno 10-step upgrade/rollback passes. |
| **M17: GhostCode Convergence & Dual-App E2E** | `GhostCode` (`convergence/ghostcode-2.0`) + `NativeShellKit` (`0.2.0-beta.1`) | Base on `fa44d1a`; port `HiberDeskPeerModule` onto `0.2.0-alpha.2` NuGet (zero submodule); adopt shared Tray/Notifications/UI 2.0; fix `ToolchainDiscoveryService` sync wait & collection thrashing; run dual-app coexistence & E2E peer suite; promote SDK to **`0.2.0-beta.1`**. | 1. GhostCode archive/diff/trash/WAL tests + 36+ isolated GUI checks pass.<br>2. Dual-app E2E test passes: HiberDesk + GhostCode concurrent launch, opt-in grant, status + notification relay, `ghostcode.open.archive` activation, revoke & rotation.<br>3. Both apps coexist with distinct data/DPAPI/tray/notification identities.<br>4. BackThread bridge (`integration/hiberdesk-bridge`) verified independently on BackThread CI. |
| **M18: Mainline Promotion & Tagging** | `NativeShellKit`, `HiberDesk`, `GhostCode` | Merge verified `convergence/*` PRs to `main` (never force-push; no public NuGet/GitHub Releases without explicit user approval); archive superseded `migration/*` and `integration/*` branches. | All `main` CI pipelines green; `DEVELOPMENT_STATE.md` updated across converged repos. |
| **M19: ED-Local-AI Adoption (Deferred)** | `ed-local-ai` *(only after §5.2 gate clears)* | Separate E1 (`.NET 10`/`WinAppSDK 2.6.5`), E2 (`CompactUtility` shell), E3 (incremental XAML views) for both `ED-Local-AI` and `Practice Fast`. | 235/235 assertions for both variants, `native-auth.dpapi` + `progress/*.json` + first-run import hash-verified, side-by-side coexistence verified. |

---

## 7. Rollback Strategies & Known Risks Register

### 7.1 Per-Milestone Rollback Paths
- **SDK (`M13`/`M15`):** Immutable prerelease versions (`0.2.0-alpha.1`, `0.2.0-alpha.2`) never mutate `0.1.0-alpha.2`. Consumers revert in 1 line in `Directory.Build.props`.
- **HiberDesk (`M14`/`M16`):** Roll back via `migration/nativeshellkit-hiberdesk` (`495e027`) or baseline `9bee11e` Inno Setup installer. Zero schema-breaking SQLite or `config.json` changes are introduced.
- **GhostCode (`M17`):** Roll back via `fa44d1a` or `v0.1.0-beta.1` (`4bc43e2`). Deleting `ghostcode-hiberdesk-v1` peer grant cleanly removes HiberDesk access.
- **BackThread:** Toggle `enableHiberDeskBridge: false` or omit `--hiberdesk`.

### 7.2 Known Risks, Tradeoffs & Escalation Triggers

| Risk / Tradeoff | Severity | Mitigation | Escalation Trigger (`REVIEW_REQUEST.md`) |
| --- | --- | --- | --- |
| **WinUI `TemplatedControl` `Generic.xbf` resolution in unpackaged `dotnet publish` & `PublishSingleFile`** | High | Hook `BeforeTargets="ComputeFilesToPublish"` in `buildTransitive/*.targets` + assert `Generic.xbf` in clean external publish test in M13/M15 CI. | If Windows App SDK 2.6.5 fails to load `Generic.xbf` from NuGet package in unpackaged single-file mode. |
| **Windows 10/11 OS Toast Visual Verification & Tray in Headless CI** | Medium | Separate *submission status* (`SubmittedToPlatform`) from *visual confirmation* (`VisualDisplayConfirmed`) as established in M11/M12; verify visually on local Windows 11 workstation at M16/M17 checkpoints. | Not a blocker for automated CI; document exact `VERIFIED` vs `NOT TESTED` split. |
| **HiberDesk `MainViewModel` Refactor Scope** | Medium | Keep `MainViewModel` public bindings intact during M14; apply SQLite/collection diffing optimizations incrementally in M16 with unit tests per domain slice. | If SQLite WAL pooling introduces locking conflicts with `HiberDesk.NativeHost` offline fallback. |
| **MEDI `ValidateScopes` Surfacing Hidden Legacy Captive Dependencies** | Medium | Run `ValidateScopes=true` in Debug/CI during M13–M14 so any hidden Singleton-depending-on-Scoped registrations in HiberDesk/GhostCode are fixed explicitly rather than crashing at runtime. | If a third-party/legacy service forces breaking lifetime restructuring. |
| **BackThread `master` vs `codex/native-foundation` Divergence** | Medium | HiberDesk consumes BackThread **only** over the versioned pipe contract (`X-BackThread-Bridge: 1`); BackThread reconciles its own `Host.Core` / iOS branches independently. | Any protocol breaking change requiring `X-BackThread-Bridge: 2`. |
| **ED-Local-AI Parallel Session Merge Collisions** | High | Strict freeze on `ed-local-ai` until active bug-fix work merges to `main`. | Never bypass freeze without explicit user sign-off. |
