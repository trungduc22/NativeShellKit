# NativeShellKit Architecture

## Layering & Dependency Graph

```
NativeShellKit.Abstractions (Microsoft.Extensions.DependencyInjection.Abstractions)
   ├── NativeShellKit.Hosting (ServiceContainer, MEDI bridge, host builder, supervision)
   ├── NativeShellKit.Security (DPAPI, safe paths, redaction, IPC HMAC auth)
   │      ├── NativeShellKit.Communication (In-process bus + authenticated Named Pipes)
   │      └── NativeShellKit.Diagnostics (Redacting logger, startup/runtime metrics, UI capture)
   ├── NativeShellKit.Windows (Win32 P/Invoke wrappers, version, Known Folders, single-instance, startup)
   │      ├── NativeShellKit.Tray (Shell_NotifyIconW, Explorer recovery, close-to-tray)
   │      └── NativeShellKit.Notifications (Windows AppNotifications + history + dedup/rate-limit)
   └── NativeShellKit.WinUI (Windows App SDK 2.6.4, MVVM, shell window, title bar, navigation, Mica/Acrylic)
          └── NativeShellKit.DesignSystem (Fluent design tokens, Light/Dark resources, accessible controls)
```

## Dependency Injection Audit & Architecture Decision (M11)

Both `GhostCode` (`GhostCode.Core`) and `HiberDesk` (`HiberDesk.Core`) already rely on `Microsoft.Extensions.DependencyInjection` (`IServiceCollection`, `IServiceScopeFactory`, `ServiceLifetime.Scoped`), so NativeShellKit's original custom `ServiceContainer` had to be audited for parity and compatibility:

| Concern | Audit Finding Before M11 | M11 Hardening Resolution |
| --- | --- | --- |
| **Scoped Services** | Missing (`Singleton` & `Transient` only) | Added `ServiceLifetimeKind.Scoped`, `IShellServiceScope`, `IShellServiceScopeFactory`, child `ScopeBucket`, and `Microsoft.Extensions.DependencyInjection.IServiceScopeFactory` support |
| **Disposal Ownership** | Caller-passed `AddSingleton(instance)` was un-disposed, while factory/type singletons disposed on root | Preserved strict standard semantics: container-created Singletons/Transients on root dispose with `ServiceContainer`; child scope instances dispose with the child scope; caller-provided instances stay owned by the caller |
| **Async Disposal** | Supported in `DisposeAsync()` for container singletons only | Child scopes implement both `IDisposable` and `IAsyncDisposable` (reverse creation order) |
| **Circular Dependency Handling** | Shared `HashSet<Type> _resolvingStack` risked stale/contended cross-thread state | Moved to per-resolution `HashSet<Type>` passed along each recursive call graph inside `_sync` |
| **Concurrent Resolution** | Guarded by `_sync` | Per-call-chain resolution state prevents cross-thread cycle false positives under concurrent resolution |
| **Registration Validation** | Abstract/interface/non-constructible types failed lazily at resolve time | Validated eagerly during `Add*` with descriptive `ArgumentException` |
| **GhostCode / HiberDesk Integration** | Required rewriting `IServiceCollection` registrations to `IServiceRegistry` | Added `Microsoft.Extensions.DependencyInjection.Abstractions` (`10.0.0`) + `MicrosoftDependencyInjectionExtensions` (`ConfigureServices(Action<IServiceCollection>)`, `PopulateFrom`, `CopyToServiceCollection`) |

### Decision: Harden + Bridge (Retain `IServiceRegistry` Lightweight Host with Official MEDI Interop)

Keeping `ServiceContainer` alongside official `Microsoft.Extensions.DependencyInjection.Abstractions` bridges existing NativeShellKit module contracts (`IShellModule.RegisterServices(IServiceRegistry)`) and zero-overhead deterministic host startup while giving `GhostCode` and `HiberDesk` first-class `IServiceCollection`, `IServiceScopeFactory`, and `Scoped` lifetime compatibility.

## Identity Isolation Guarantee

Every consumer provides an `AppIdentity(AppId, DisplayName, Publisher, Version)`:
- **Paths:** `%LOCALAPPDATA%\<Publisher>\<AppId>\...` (overridable root for isolated tests)
- **Single-instance Mutex:** `Local\NativeShellKit_<SanitizedAppId>_SingleInstance_Mutex_v1`
- **Activation & IPC Pipes:** `NativeShellKit_<SanitizedAppId>_Activation_Pipe_v1`, `NativeShellKit_<SanitizedAppId>_Ipc_Pipe_v1`
- **Startup Run Key:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` -> `NativeShellKit.<SanitizedAppId>`
- **Tray & Installer GUIDs:** Deterministic SHA-256 derived GUIDs per `AppUserModelId`
- **Notification Isolation:** Every toast/banner payload tags `appId=<AppId>` and rejects cross-app activations.
