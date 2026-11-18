# Compatibility Guide: BackThread & Non-WinUI Integration

## Core Principle

**BackThread must NOT be forcibly migrated to WinUI 3.** `BackThread` uses a separate Windows desktop UI approach alongside C/iOS components. NativeShellKit enforces strict layering so non-UI foundational services work directly inside non-WinUI .NET Windows processes without pulling in XAML or WinUI runtime dependencies.

## Safe Non-UI Package Surface for BackThread

`BackThread` can consume ONLY these UI-agnostic packages (verified zero dependency on `Microsoft.WindowsAppSDK` WinUI XAML):

| Package | Capabilities Available Without WinUI 3 |
| --- | --- |
| `NativeShellKit.Abstractions` | `AppIdentity`, `IServiceRegistry`, `ILifecycleHook`, `IBackgroundTaskSupervisor`, `ISettingsService`, `IInProcessEventBus`, `IIpcServer`/`IIpcClient`, `INotificationService` contracts |
| `NativeShellKit.Hosting` | `NativeShellHostBuilder`, `ServiceContainer`, `JsonFileSettingsStore<T>`, `BackgroundTaskSupervisor`, `ModuleCatalog` |
| `NativeShellKit.Windows` | `WindowsVersionService`, `KnownFoldersService`, `SingleInstanceCoordinator`, `WindowsStartupService`, `ProtocolActivationService`, `NetworkAndPowerMonitor` |
| `NativeShellKit.Security` | `DpapiSecretStore`, `IpcAuthManager`, `SafePathValidator`, `SecretRedactor` |
| `NativeShellKit.Communication` | `InProcessEventBus`, `NamedPipeIpcServer`, `NamedPipeIpcClient` |
| `NativeShellKit.Diagnostics` | `RedactingFileLogger`, `DiagnosticsService`, `UiSelfTestRunner` |

## Packages BackThread Should NOT Reference

- `NativeShellKit.WinUI` (requires WinUI 3 XAML runtime)
- `NativeShellKit.DesignSystem` (requires WinUI 3 XAML resource dictionaries & controls)

*(Note: `NativeShellKit.Tray` depends only on `NativeShellKit.Abstractions` and `NativeShellKit.Windows` via `IWin32Interop`, so even WinForms/`HWND`-based hosts can integrate `NativeTrayService` if desired, or keep `BackThread`'s existing tray code untouched.)*

## Suggested Non-UI Host Pattern in BackThread

```csharp
var identity = new AppIdentity("backthread", "BackThread", "trungduc22");
var builder = NativeShellHostBuilder.Create(identity, args)
    .ConfigureServices(services =>
    {
        services.AddWindowsServices();
        services.AddSecurityAndDiagnostics();
        services.AddCommunicationServices();
    });
```

This lets `BackThread` adopt unified settings, DPAPI secret storage, redacted logging, single-instance coordination, and authenticated named-pipe IPC while keeping its existing desktop UI and cross-platform architecture intact.
