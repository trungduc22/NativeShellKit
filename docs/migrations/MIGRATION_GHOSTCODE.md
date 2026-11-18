# Migration Guide: GhostCode -> NativeShellKit

## Target Overview

`GhostCode` already aligns closely with NativeShellKit's runtime baseline (`net10.0-windows10.0.19041.0`, unpackaged WinUI 3, Windows App SDK). NativeShellKit replaces duplicated shell plumbing inside `GhostCode` with shared NuGet libraries while keeping `GhostCode` domain features isolated in its own repository.

## Component Mapping

| GhostCode Custom Implementation | NativeShellKit Replacement | Package |
| --- | --- | --- |
| Custom app startup & DI container setup | `NativeShellHostBuilder` + `AppIdentity("ghostcode", "GhostCode", "trungduc22")` | `NativeShellKit.Hosting` |
| `GhostThemePalette` / theme switching | `WinUiThemeService` (`IThemeService`) + `DesignTokens` + `DesignSystemThemeResources` | `NativeShellKit.WinUI`, `NativeShellKit.DesignSystem` |
| `AppNotificationManager` wrapper | `UnifiedNotificationService` (`AddUnifiedNotifications()`) + `NotificationBannerControl` | `NativeShellKit.Notifications`, `NativeShellKit.DesignSystem` |
| Custom `Shell_NotifyIconW` P/Invoke wrapper | `NativeTrayService` + `IWin32Interop` (`TaskbarCreated` recovery + `NIF_GUID`) | `NativeShellKit.Tray`, `NativeShellKit.Windows` |
| `IncludeCompiledXamlInPublish` target workaround | Automatic `buildTransitive\<PackageId>.targets` shipped inside WinUI & DesignSystem packages | `NativeShellKit.WinUI`, `NativeShellKit.DesignSystem` |
| `UiSelfTestRunner` copy | `UiSelfTestRunner` + `DiagnosticsService` + `RedactingFileLogger` | `NativeShellKit.Diagnostics` |
| `packaging/GhostCode.iss` generation | `nsk generate-installer --app-id ghostcode --app-name GhostCode --exe GhostCode.exe` | `NativeShellKit.DevTools` |

## Recommended Incremental Migration Steps

1. Add `NativeShellKitLocalFeed` NuGet source (`artifacts\packages` or Developer Kit `packages\`).
2. Add `<PackageReference Include="NativeShellKit.*" Version="0.1.0-alpha.1" />` packages incrementally (`Abstractions` -> `Hosting` -> `Windows`/`Tray` -> `Notifications` -> `WinUI`/`DesignSystem` -> `Diagnostics`).
3. Keep `AppIdentity("ghostcode", "GhostCode", "trungduc22")` consistent so settings, DPAPI secrets, logs, and notifications resolve cleanly under isolated `ghostcode` paths (and migrate any legacy settings filename via one-time startup copy if preserving existing user configs).
4. Replace custom `AppNotificationManager` and `Shell_NotifyIconW` wrappers with `AddUnifiedNotifications()` and `NativeTrayService`.
5. Run `GhostCode.exe --self-test` powered by `UiSelfTestRunner` and validate packaging artifacts with `ReleasePackagingValidator`.

## Do Not Break

- Keep `GhostCode`'s Git remote, app identity, and custom editor/workspace logic completely independent of `HiberDesk` and `ED-Local-AI`.
