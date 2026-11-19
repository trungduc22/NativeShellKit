# NativeShellKit Agent & Contributor Guide

## Repository Conventions

1. **Canonical Contract Rule:** Keep one canonical abstraction per cross-cutting concern (`IAppHost`, `AppIdentity`, `INotificationService`, `INavigationService`, `IThemeService`, `ISettingsStore<T>`, `ITrayService`, `IInProcessEventBus`). Do not duplicate abstractions across packages.
2. **XAML-First Native UI:** WinUI 3 layouts, styles, resource dictionaries, and visual states live in XAML/WinUI controls under `NativeShellKit.WinUI` and `NativeShellKit.DesignSystem`. Business logic, services, DI, lifecycle, and Win32 interop stay in C#. Never use Electron or WebView2 as the desktop shell.
3. **Minimal Dependency Footprint:**
   - `NativeShellKit.Abstractions` must depend only on `Microsoft.Extensions.DependencyInjection.Abstractions` beyond the .NET BCL (zero WinUI, zero Win32, zero third-party packages) and multi-target `net10.0;net10.0-windows10.0.19041.0` per ADR-005.
   - Non-UI modules (`Hosting`, `Windows`, `Security`, `Communication`, `Diagnostics`, `Build`) must not depend on `Microsoft.WindowsAppSDK` / WinUI XAML so headless tools and non-WinUI apps (such as `BackThread`) can consume them cleanly.
4. **Application Identity Isolation:** Never hardcode consumer names (`GhostCode`, `HiberDesk`, `ED-Local-AI`, `BackThread`) inside reusable SDK assemblies. All data directories, mutexes, Named Pipes, registry values, tray GUIDs, DPAPI entropy, and notification arguments must derive deterministically from `AppIdentity`.
5. **Security & Public Repository Rules:**
   - Never commit `.dpapi`, `.db`, `.pfx`, `.snk`, `.log`, API keys, tokens, or personal files.
   - Always inspect `git status` and `git diff --cached` before committing and pushing.
   - Never expose unauthenticated command execution over Named Pipes; all IPC handlers must validate protocol version, HMAC authentication, and explicit operation allowlists.
   - Never run `git push --force` without explicit authorization.
   - Never create GitHub Releases or publish to NuGet.org without explicit authorization.

## Build & Test Workflow

- Build solution: `dotnet build NativeShellKit.sln -c Release -p:Platform=x64`
- Run tests: `dotnet run --project tests/NativeShellKit.Tests/NativeShellKit.Tests.csproj -c Release`
- Pack NuGet libraries: `./packaging/Pack-NuGet.ps1`
- Build Developer Kit: `./packaging/Build-DeveloperKit.ps1`

## Resumability

Always update `docs/DEVELOPMENT_STATE.md` at the end of every milestone before committing and pushing to `origin main`.
