# NativeShellKit

A unified, XAML-first, C#-driven native Windows application SDK, WinUI 3 shell, design system, Windows system integration toolkit, and modular runtime foundation for Windows 10/11 (x64).

## Target Ecosystem

- **GhostCode** — Native WinUI 3 code companion & lossless archive ([Migration Guide](docs/migrations/MIGRATION_GHOSTCODE.md))
- **HiberDesk** — Native WinUI 3 workspace & background companion ([Migration Guide](docs/migrations/MIGRATION_HIBERDESK.md))
- **ED-Local-AI** — Native WinUI 3 local assistant ([Migration Guide](docs/migrations/MIGRATION_ED_LOCAL_AI.md))
- **BackThread** — Non-WinUI desktop/cross-platform companion using UI-agnostic foundational packages ([Compatibility Guide](docs/migrations/MIGRATION_BACKTHREAD_COMPATIBILITY.md))

## Package Suite (`0.1.0-alpha.1`)

- `NativeShellKit.Abstractions` — Application identity (`AppIdentity`), lifecycle, module, notification, navigation, settings, diagnostics, and IPC contracts.
- `NativeShellKit.Hosting` — `NativeShellHostBuilder`, lightweight DI container (`ServiceContainer`), atomic JSON settings store, background task supervision, and ordered lifecycle management.
- `NativeShellKit.Windows` — Win32 P/Invoke adapters, Windows version detection, Known Folders, single-instance mutex + Named Pipe activation, startup registration, and protocol activation.
- `NativeShellKit.WinUI` — Native WinUI 3 shell (`NativeShellView`, `CustomTitleBarControl`, `ShellWindowService`), navigation service, theme/Mica/Acrylic coordinator, dialog coordinator, and MVVM primitives (includes `buildTransitive` `.xbf` deployment targets).
- `NativeShellKit.DesignSystem` — Fluent design tokens (`DesignTokens`), Light/Dark resource dictionaries, and accessible WinUI 3 controls (`PageHeaderControl`, `CompactMetricCard`, `SettingsSectionControl`, `SettingsRowControl`, `StatusIndicatorControl`, `NotificationBannerControl`, `SearchBarControl`, `StateViewControl`).
- `NativeShellKit.Notifications` — Unified Windows app & in-app notification coordinator (`UnifiedNotificationService`) with deduplication, rate limiting, quiet hours, action validation, cross-app isolation, and JSON history.
- `NativeShellKit.Tray` — Native Win32 `Shell_NotifyIconW` tray icon manager with context menus, close-to-tray, and `TaskbarCreated` recovery.
- `NativeShellKit.Communication` — Typed in-process event bus (`InProcessEventBus`) and HMAC-authenticated Named Pipe IPC (`NamedPipeIpcServer` / `NamedPipeIpcClient`, protocol `v1.0`).
- `NativeShellKit.Security` — Per-identity DPAPI secret storage (`DpapiSecretStore`), safe path validation (`SafePathValidator`), secret redaction (`SecretRedactor`), and `IpcAuthManager`.
- `NativeShellKit.Diagnostics` — Redacting file logger (`RedactingFileLogger`), startup timing tracker, runtime metrics (`DiagnosticsService`), UI self-tests (`UiSelfTestRunner`), and window screenshot capture.
- `NativeShellKit.Build` — NuGet archive validator, SHA-256 checksums, and forbidden artifact scanner (`ReleasePackagingValidator`).
- `NativeShellKit.Templates` — Installable `dotnet new nativeshellkit` WinUI 3 application template.
- `NativeShellKit.DevTools` — `nsk` developer CLI (`info`, `doctor`, `validate-identity`, `generate-installer`).

## Quick Start

### Build, Pack, Test, and Stage Developer Kit

```powershell
# 1. Pack all 13 NuGet libraries & template package into artifacts/packages
.\packaging\Pack-NuGet.ps1 -Configuration Release -Platform x64

# 2. Build full solution (SDK + CLI + samples + installer + tests)
dotnet restore NativeShellKit.sln -p:RestoreAdditionalProjectSources="$PWD\artifacts\packages"
dotnet build NativeShellKit.sln -c Release -p:Platform=x64 --no-restore

# 3. Run automated xUnit test suite
dotnet test tests\NativeShellKit.Tests\NativeShellKit.Tests.csproj -c Release -p:Platform=x64 --no-build

# 4. Build Developer Kit payload & setup executable into artifacts/developer-kit
.\packaging\Build-DeveloperKit.ps1 -Configuration Release -Platform x64
```

### Scaffold a New Application

```powershell
dotnet new install .\artifacts\packages\NativeShellKit.Templates.0.1.0-alpha.1.nupkg
dotnet new nativeshellkit -n MyNextApp -o .\MyNextApp
```

### Reference Samples

- `samples/MinimalNativeApp` — Minimal custom-titlebar WinUI 3 host with 3 routes, DesignSystem controls, Light/Dark/System switching, and settings persistence (`--self-test`).
- `samples/BackgroundCompanionApp` — Single-instance coordinator, auto-start registration, tray menu commands, supervised background heartbeat, and quiet-hours/dedup notifications (`--self-test`).
- `samples/UnifiedHostDemo` — Modular host loading two independent `IShellModule`s communicating over `IInProcessEventBus` and `INotificationService` (`--self-test`).

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Versioning & Upgrade Policy](docs/VERSIONING_AND_UPGRADE_POLICY.md)
- [Development State](docs/DEVELOPMENT_STATE.md)
- [Agent Guide](AGENTS.md)

## License

MIT — see [LICENSE](LICENSE).
