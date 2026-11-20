# NativeShellKit 2.0 — Architecture Decision Records (ADRs)

**Status:** Approved Decision Log (`docs/architecture-2.0`)  
All decisions below are derived from direct inspection of `NativeShellKit`, `HiberDesk`, `GhostCode`, `BackThread`, and `ed-local-ai`.

---

## ADR-001: Maintain Five Independent Product Repositories (No Monorepo Merge)
- **Context:** The ecosystem spans a shared SDK (`NativeShellKit`) and four distinct products (`HiberDesk`, `GhostCode`, `ED-Local-AI`, `BackThread`) with different release cadences, platform targets (`.NET 10` WinUI 3, `.NET 8` WinUI 3, `.NET 8` + C/Obj-C iOS), and independent user bases.
- **Decision:** Keep all five repositories separate. Share platform capabilities exclusively through versioned `NativeShellKit.*` NuGet packages and explicitly versioned local IPC contracts.
- **Consequences:** Independent release cycles, clean security boundaries, zero cross-repo build breakage (especially protecting `ed-local-ai`'s active bug-fix branch and `BackThread`'s iOS/audio toolchain).

## ADR-002: NuGet-Only SDK Consumption (Ban `vendor/NativeShellKit` Git Submodules)
- **Context:** `migration/nativeshellkit-hiberdesk` (`495e027`) and `migration/nativeshellkit-ghostcode` (`fa44d1a`) consume pinned NuGet packages (`0.1.0-alpha.2`), whereas `integration/backthread-ghostcode-hub` (`d196a9f`) and `integration/hiberdesk-events` (`a19742e`) introduced a `.gitmodules` submodule at `vendor/NativeShellKit` (`7c3e34c`) using `<ProjectReference>`.
- **Decision:** Ban Git submodules for SDK consumption across all consumer repositories. Delete `.gitmodules` and `vendor/NativeShellKit` during convergence; consume `NativeShellKit.*` strictly via exact-pinned immutable NuGet packages (`[0.2.0-alpha.1]+`) backed by SHA-256 verified feeds in CI.
- **Consequences:** Eliminates diamond assembly/XBF/PRI conflicts, guarantees reproducible builds, enforces public API boundaries, and validates real NuGet packaging targets continuously.

## ADR-003: Converge on Immutable Prerelease `0.2.0-alpha.1` (Never Reuse `0.1.0-alpha.2`)
- **Context:** `0.1.0-alpha.1` (`main`) and `0.1.0-alpha.2` (`migration/m12-sdk`) have already been packed, checksummed, and verified by HiberDesk and GhostCode M12 pilots. Converging `feature/ecosystem-integration-contracts` (`LocalPeerGrant`) and the 2.0 host reconstruction changes public APIs and binaries.
- **Decision:** Freeze `0.1.0-alpha.2` permanently for rollback. Publish the converged baseline as **`0.2.0-alpha.1`** (followed by `0.2.0-alpha.2` for UI/UX 2.0 and `0.2.0-beta.1` for dual-app verification). Enforce single-source versioning from `Directory.Build.props` across all scripts, installers, templates, and CI.
- **Consequences:** Zero NuGet cache poisoning, deterministic SHA-256 feed verification, trivial 1-line consumer rollback.

## ADR-004: Back `NativeShellHost` on `Microsoft.Extensions.DependencyInjection.ServiceProvider`
- **Context:** `NativeShellKit.Hosting` already depends on `Microsoft.Extensions.DependencyInjection 10.0.0`, yet `NativeShellHostBuilder.Build()` instantiates a custom `ServiceContainer` (`ServiceContainer.cs`) that holds `lock (_sync)` across constructor/factory invocation (deadlock hazard), omits root-scope captive dependency validation, and leaks disposables for unregistered concrete types.
- **Decision:** Replace `ServiceContainer`'s runtime resolution engine inside `NativeShellHost` with `Microsoft.Extensions.DependencyInjection.ServiceProvider` (with `ValidateScopes = true` and `ValidateOnBuild = true` in Debug/test builds). Keep `IServiceRegistry` in `NativeShellKit.Abstractions` as a thin, source-compatible adapter delegating to `IServiceCollection`.
- **Consequences:** Eliminates deadlock and disposal bugs immediately, unlocks open generics and keyed services, guarantees 100% behavioral parity with standard .NET DI, and preserves source compatibility for `IShellModule.RegisterServices(IServiceRegistry)`.

## ADR-005: Formalize `NativeShellKit.Abstractions` Dependency & Multi-Target `net10.0`
- **Context:** `AGENTS.md` Rule 3 originally prescribed zero external dependencies in `NativeShellKit.Abstractions`, while M11 added `Microsoft.Extensions.DependencyInjection.Abstractions 10.0.0` (acknowledged as a versioned contract in `M12_STATE.md`). Simultaneously, `NativeShellKit.Abstractions` and `NativeShellKit.Hosting` target only `net10.0-windows10.0.19041.0` despite containing zero Windows-specific APIs, blocking pure `net10.0` libraries (`HiberDesk.Core`, `GhostCode.Core`, `HiberDesk.Integrations.BackThread`) from referencing SDK abstractions.
- **Decision:**
  1. Explicitly permit `Microsoft.Extensions.DependencyInjection.Abstractions` as the sole BCL-tier Microsoft abstractions dependency of `NativeShellKit.Abstractions` (zero WinUI, zero Win32, zero third-party packages).
  2. Multi-target `NativeShellKit.Abstractions` and `NativeShellKit.Hosting` to **`net10.0;net10.0-windows10.0.19041.0`** (and target `NativeShellKit.Build` to `net10.0`).
- **Consequences:** `HiberDesk.Core`, `GhostCode.Core`, and Linux CI test runners can directly depend on canonical abstractions (`IInProcessEventBus`, `ISdkLogger`, `ISettingsStore<T>`, `INotificationService`) without pulling Windows TFMs.

## ADR-006: Single Authoritative Host & Failure-Isolated `IShellModule` v2
- **Context:** `HiberDesk [integration/backthread-ghostcode-hub]` created a second `NativeShellHost` and second `UnifiedNotificationService` inside `EcosystemRuntime.cs` to host BackThread and GhostCode modules.
- **Decision:** Enforce strictly **one `IAppHost` / `IServiceProvider` / `INotificationService` per process**. Extend `IShellModule` with `StartupOrder`, `IsCritical`, `DependsOnModuleIds`, and `ModuleHealthState`. Non-critical integration modules (`BackThreadIntegrationModule`, `GhostCodeIntegrationModule`, `HiberDeskPeerModule`) initialize asynchronously after first-frame render and isolate failures to `ModuleHealthState.Faulted`/`Degraded` (rendered via `ModuleCardBoundary`) without disrupting the core application.
- **Consequences:** Eliminates split DI graphs, duplicate notification history files, and duplicate `PublishAsync` calls; keeps cold startup fast even when companion processes are offline.

## ADR-007: `LegacyIdentityOverrides` for Zero-Disruption App Migration
- **Context:** `AppIdentity` deterministically derives `Local\NativeShellKit_<id>_SingleInstance_Mutex_v1`, `NativeShellKit.<id>` Startup Run value, and `NativeShellKit_<id>_Activation_Pipe_v1`. Existing installed apps use `HiberDesk` (`--background`), `Local\HiberDesk_SingleInstance_Mutex_v1`, `HiberDesk_SingleInstance_Pipe_v1`, `EDLocalAI.Native.<user>`, and `EDLocalAI.PracticeFast.<user>`, forcing HiberDesk M12 to keep legacy startup/single-instance services parallel to the SDK.
- **Decision:** Add optional `LegacyIdentityOverrides` to `AppIdentity` / host startup configuration so `NativeShellKit.Windows` (`IStartupService`, `ISingleInstanceService`) natively honors existing registry value names, CLI flags, mutexes, and activation pipes while keeping deterministic `AppIdentity` defaults for new applications.
- **Consequences:** HiberDesk, GhostCode, and future ED-Local-AI can retire duplicate startup/single-instance implementations without stranding existing users' auto-start entries or breaking single-instance handoff across upgrades.

## ADR-008: Three-Plane IPC Boundary (No Forced Unification Across Incompatible Protocols)
- **Context:** Three distinct local IPC mechanisms exist: browser Native Messaging (`com.hiberdesk.nativehost`, `com.ghostcode.bridge`), same-user ecosystem peer IPC (`LocalPeerGrant`), and BackThread's local companion pipe (`BackThread.HiberDesk.v1.<SID>.<port>`).
- **Decision:** Keep the three planes explicitly separated:
  - Browser Native Messaging stays app-owned.
  - Ecosystem SDK peer IPC (`GhostCode <-> HiberDesk`) uses `NativeShellKit.Communication.LocalPeerGrant` (`PipeOptions.CurrentUserOnly`, DPAPI 32-byte key, mutual HMAC-SHA256 signing, `MessageId` correlation, bounded 3s timeout, replay-nonce cache, operation allowlist).
  - BackThread companion IPC stays BackThread-owned over its dedicated HMAC-derived-bearer named pipe with zero NativeShellKit dependency in BackThread.
- **Consequences:** Zero coupling imposed on BackThread's real-time audio/USB host; zero risk of breaking browser extensions; strong cryptographic isolation for every cross-app boundary.

## ADR-009: XAML-First Fluent 2 Design System (`TemplatedControl` + `DependencyProperty` + `HighContrast`)
- **Context:** v1 `NativeShellKit.DesignSystem` controls had zero `DependencyProperty` definitions, hardcoded `Color.FromArgb` in C# (`StatusIndicatorControl`), hardcoded English strings (`"Retry"`), `Opacity` hacks for secondary text, and no `HighContrast` `ThemeDictionary`.
- **Decision:** Rebuild `NativeShellKit.DesignSystem` (`0.2.0-alpha.2`) as XAML-first `TemplatedControl` primitives (`Themes/Generic.xaml`) with 100% `DependencyProperty`-backed APIs, three explicit `ThemeDictionaries` (`Light`, `Dark`, `HighContrast`), semantic `Nsk*` surface/text/status tokens, Standard & Compact density dictionaries, and zero hardcoded locale copy. Preserve v1 `NativeShell*` resource keys as aliases through `0.2.x`.
- **Consequences:** Full `{x:Bind}` MVVM support, true Light/Dark/HighContrast responsiveness, bilingual En/Vi compatibility, and clean customization without forking SDK controls.

## ADR-010: Single Unified `NativeShellFrame` Supporting Five Shell Variants
- **Context:** The ecosystem needs five desktop shell archetypes (Dashboard, Module-Host, Compact Utility, Single-Page, Tray-First) without maintaining five unrelated shell frameworks.
- **Decision:** Build one `NativeShellFrame` control in `NativeShellKit.WinUI` configured via `ShellMode`, `ShellDensity`, and composable XAML slots (`TitleBarSearchContent`, `TitleBarStatusContent`, `TitleBarActionsContent`, `FooterStripContent`) paired with `ShellTitleBar` (auto `RightInset` + `InputNonClientPointerSource` passthrough rect registration) and `ITrayService` integration.
- **Consequences:** HiberDesk (`ModuleHost` + `TrayFirst`), GhostCode (`Dashboard` + `TrayFirst`), future ED-Local-AI (`CompactUtility`), and tooling (`SinglePage`) all share one theme, navigation, dialog, notification, and DPI engine while preserving distinct product ergonomics.

## ADR-011: Evidence-Driven Performance Architecture (No Artificial Memory Hacks)
- **Context:** Source audits revealed concrete bottlenecks: 4 PRAGMAs per SQLite query + un-debounced `RefreshAllAsync` in HiberDesk, `.Clear()` + re-add loops across 28+ `ObservableCollection`s in HiberDesk & GhostCode, unbounded per-event `Task.Run` in `SafeFileOrganizer`'s 8 KiB `FileSystemWatcher`, 256-entry hard-cap drop bug in `EcosystemRuntime._seen`, and unsubscribed event handlers + page rebuilds per tab click.
- **Decision:** Enforce six structural fixes with Release x64 numeric budgets (`PLATFORM_2_0.md` §5.2): one-time SQLite WAL init + pooled batched snapshot reads, 250ms single-flight UI refresh debouncing, `ObservableCollectionSync.ApplyKeyedDiff`, 64 KiB coalesced single-worker `FileSystemWatcher` pipeline on `IBackgroundTaskSupervisor`, LRU+TTL relay dedup ring buffer, and cached primary shell routes with deterministic event cleanup.
- **Consequences:** Predictable sub-16ms UI responsiveness, bounded thread/memory footprint, zero event drops under file bursts, and measurable CI regression gates.

## ADR-012: Freeze ED-Local-AI During Convergence; Defer Migration Until Post-Stabilization Gate
- **Context:** Another active AI session is executing bug fixes on `ed-local-ai` (`.NET 8`, `WinAppSDK 1.8.260921001`, two independent single-file variants with C#-built compact UI).
- **Decision:** Freeze `ed-local-ai` during M13–M18. Defer its migration to M19 behind explicit prerequisites (bug-fix merge to `main`, `0.2.0-beta.1` dual-app verification, `PublishSingleFile` XBF compatibility check) and preserve both data roots, first-run import, `null`-entropy `native-auth.dpapi`, and both variant mutexes.
- **Consequences:** Zero merge conflicts with the active ED-Local-AI session, unblocked HiberDesk/GhostCode 2.0 convergence, and a clean `CompactUtility` landing zone when ED-Local-AI is ready.
