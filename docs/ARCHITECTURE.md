# NativeShellKit Architecture

## Layering & Dependency Graph

```
NativeShellKit.Abstractions (no external NuGet deps)
   ├── NativeShellKit.Hosting (DI container, host builder, supervision)
   ├── NativeShellKit.Security (DPAPI, safe paths, redaction, IPC HMAC auth)
   │      ├── NativeShellKit.Communication (In-process bus + authenticated Named Pipes)
   │      └── NativeShellKit.Diagnostics (Redacting logger, startup/runtime metrics, UI capture)
   ├── NativeShellKit.Windows (Win32 P/Invoke wrappers, version, Known Folders, single-instance, startup)
   │      ├── NativeShellKit.Tray (Shell_NotifyIconW, Explorer recovery, close-to-tray)
   │      └── NativeShellKit.Notifications (Windows AppNotifications + history + dedup/rate-limit)
   └── NativeShellKit.WinUI (Windows App SDK 2.6.4, MVVM, shell window, title bar, navigation, Mica/Acrylic)
          └── NativeShellKit.DesignSystem (Fluent design tokens, Light/Dark resources, accessible controls)
```

## Identity Isolation Guarantee

Every consumer provides an `AppIdentity(AppId, DisplayName, Publisher, Version)`:
- **Paths:** `%LOCALAPPDATA%\<Publisher>\<AppId>\...` (overridable root for isolated tests)
- **Single-instance Mutex:** `Local\NativeShellKit_<SanitizedAppId>_SingleInstance_Mutex_v1`
- **Activation & IPC Pipes:** `NativeShellKit_<SanitizedAppId>_Activation_Pipe_v1`, `NativeShellKit_<SanitizedAppId>_Ipc_Pipe_v1`
- **Startup Run Key:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` -> `NativeShellKit.<SanitizedAppId>`
- **Tray & Installer GUIDs:** Deterministic SHA-256 derived GUIDs per `AppUserModelId`
- **Notification Isolation:** Every toast/banner payload tags `appId=<AppId>` and rejects cross-app activations.
