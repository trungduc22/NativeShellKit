# Migration Guide: HiberDesk -> NativeShellKit

## Target Overview

`HiberDesk` runs on `net10.0-windows10.0.19041.0` with native background lifecycle, single-instance activation, startup registration, system tray control, and runtime performance probes. NativeShellKit consolidates all of those infrastructure services under clean, interface-driven abstractions.

## Component Mapping

| HiberDesk Custom Implementation | NativeShellKit Replacement | Package |
| --- | --- | --- |
| `SingleInstanceCoordinator` | `NativeShellKit.Windows.SingleInstance.SingleInstanceCoordinator` | `NativeShellKit.Windows` |
| `WindowsAutoStartService` | `NativeShellKit.Windows.Startup.WindowsStartupService` + `IStartupRegistryBackend` | `NativeShellKit.Windows` |
| `TrayIconManager` | `NativeShellKit.Tray.NativeTrayService` + `TrayMenuItem` | `NativeShellKit.Tray` |
| `PerformanceProbe` | `DiagnosticsService` (`CaptureMetrics()`, `RecordStartupPhase()`, `GetStartupTimings()`) | `NativeShellKit.Diagnostics` |
| Custom settings/logs paths | `AppIdentity("hiberdesk", "HiberDesk", "trungduc22")` + `ISettingsService` | `NativeShellKit.Abstractions`, `NativeShellKit.Hosting` |
| `packaging/HiberDesk.iss` boilerplate | `nsk generate-installer --app-id hiberdesk --app-name HiberDesk --exe HiberDesk.exe` | `NativeShellKit.DevTools` |

## Recommended Incremental Migration Steps

1. Introduce `AppIdentity("hiberdesk", "HiberDesk", "trungduc22")` in `Program.cs` before UI initialization.
2. Swap `HiberDesk`'s single-instance mutex/pipe code for `SingleInstanceCoordinator(identity)` and attach `ActivationCommandReceived` to `IAppLifecycleService.RaiseActivationAsync`.
3. Migrate `WindowsAutoStartService` to `WindowsStartupService(identity)` (use `InMemoryStartupRegistryBackend` in automated tests to keep CI clean).
4. Replace `TrayIconManager` with `NativeTrayService`, routing `RestoreRequested` to `IShellWindowService.ShowAndActivate()` and `FullExitRequested` to `IAppLifecycleService.RequestShutdownAsync()`.
5. Migrate `PerformanceProbe` call sites to `IDiagnosticsService` and wrap background loops in `IBackgroundTaskSupervisor.RunSupervisedAsync`.

## Side-by-Side Coexistence Verification

With `AppIdentity("hiberdesk", ...)`, HiberDesk's `TrayIconGuid`, `SingleInstanceMutexName`, `ActivationPipeName`, `StartupRegistryValueName`, and `%LOCALAPPDATA%` folders never collide with `GhostCode` or `ED-Local-AI` even when all applications run concurrently.
