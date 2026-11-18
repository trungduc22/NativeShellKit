# NativeShellKit

A unified, XAML-first, C#-driven native Windows application SDK, WinUI 3 shell, design system, Windows system integration toolkit, and modular runtime foundation for Windows 10/11 (x64).

## Vision

NativeShellKit provides one canonical architectural contract across:
- **GhostCode** (WinUI 3 code companion & lossless archive)
- **HiberDesk** (WinUI 3 workspace & background companion)
- **ED-Local-AI** (WinUI 3 local assistant)
- **BackThread** (compatible with non-UI foundational services)

## Planned Package Architecture (`0.1.0-alpha.1`)

- `NativeShellKit.Abstractions` — Application identity, lifecycle, module, notification, navigation, settings, diagnostics, and IPC contracts.
- `NativeShellKit.Hosting` — Host builder, lightweight DI container, background task supervision, and ordered lifecycle management.
- `NativeShellKit.Windows` — Win32 P/Invoke wrappers, version detection, Known Folders, single-instance mutex + Named Pipe activation, startup registration, and system adapters.
- `NativeShellKit.WinUI` — Native application shell, custom title bar, navigation service, theme/Mica/Acrylic coordinator, dialog coordinator, and MVVM primitives.
- `NativeShellKit.DesignSystem` — Fluent design tokens, semantic theme brushes, typography, and accessible WinUI 3 controls.
- `NativeShellKit.Notifications` — Canonical Windows app & in-app notification coordinator with deduplication, rate limiting, action validation, identity isolation, and history.
- `NativeShellKit.Tray` — Native Win32 `Shell_NotifyIconW` tray icon manager with context menus, close-to-tray, and Explorer restart recovery.
- `NativeShellKit.Communication` — Typed in-process event bus and authenticated, allowlisted Named Pipe IPC (`v1.0`).
- `NativeShellKit.Security` — Per-identity DPAPI secret storage, safe path validation, secret redaction, and HMAC IPC auth.
- `NativeShellKit.Diagnostics` — Redacting file logger, startup timing tracker, runtime metrics, UI self-tests, and screenshot capture.
- `NativeShellKit.Build` — NuGet packaging validation, checksums, and release hygiene tooling.
- `NativeShellKit.Templates` — Installable `dotnet new nativeshellkit` templates.
- `NativeShellKit.DevTools` — `nsk` developer CLI and installer generator.

## Status

See [docs/DEVELOPMENT_STATE.md](docs/DEVELOPMENT_STATE.md) for current milestone progress, verified commits, and build/test commands.

## License

MIT — see [LICENSE](LICENSE).
