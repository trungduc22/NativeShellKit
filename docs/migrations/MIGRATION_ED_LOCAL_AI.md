# Migration Guide: ED-Local-AI -> NativeShellKit

## Target Overview

`ED-Local-AI-WinUI3` currently targets `.NET 8`, manually creates a `Mutex` in `Program.Main`, initializes `ComWrappersSupport`, handles `--self-test`, and protects local auth credentials with `ProtectedData` (DPAPI). Migrating to NativeShellKit standardizes host startup, secrets isolation, IPC authentication, and fluent design tokens.

## Component Mapping

| ED-Local-AI Current Pattern | NativeShellKit Replacement | Package |
| --- | --- | --- |
| `<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>` | Upgrade TFM to `net10.0-windows10.0.19041.0` + `Microsoft.WindowsAppSDK 2.6.4` | SDK Baseline |
| Manual `Mutex` single-instance guard in `Program.Main` | `SingleInstanceCoordinator` with `AppIdentity("ed-local-ai", "ED Local AI")` | `NativeShellKit.Windows` |
| Manual `ProtectedData` local auth helper | `DpapiSecretStore` (`NativeShellKit.DPAPI.v1:<AUMID>` per-app entropy) + `SafePathValidator` | `NativeShellKit.Security` |
| Ad-hoc log strings | `RedactingFileLogger` + `SecretRedactor` (auto-redacts API keys, `Bearer`, `sk-`, `AIza`, `ghp_`) | `NativeShellKit.Security`, `NativeShellKit.Diagnostics` |
| Local worker/IPC wiring | `IInProcessEventBus` + `NamedPipeIpcServer` / `NamedPipeIpcClient` + `IpcAuthManager` | `NativeShellKit.Communication` |
| Custom `--self-test` harness | `AppStartupContext.IsSelfTestMode` + `UiSelfTestRunner` | `NativeShellKit.Hosting`, `NativeShellKit.Diagnostics` |
| Unpackaged WinUI title bar / navigation chrome | `AddWinUiShell()`, `NativeShellView`, `CustomTitleBarControl`, `DesignSystemThemeResources` | `NativeShellKit.WinUI`, `NativeShellKit.DesignSystem` |

## Recommended Migration Steps

1. Update `EDLocalAI.csproj` `<TargetFramework>` to `net10.0-windows10.0.19041.0` while preserving `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, and `DISABLE_XAML_GENERATED_MAIN`.
2. Build `NativeShellHostBuilder.Create(new AppIdentity("ed-local-ai", "ED Local AI", ...), args)` inside `Program.Main`, keeping `ComWrappersSupport.InitializeComWrappers()` and `Application.Start` cleanly for interactive mode while routing `--self-test` via `AppStartupContext.IsSelfTestMode`.
3. Move API keys / local pairing secrets into `DpapiSecretStore` (or load existing keys during a one-time migration check) and replace plain logger sinks with `RedactingFileLogger` so local model tokens are never written to disk unredacted.
4. Adopt `NamedPipeIpcServer` + `IpcAuthManager` for any local CLI/agent-to-UI companion calls.
5. Validate local build outputs with `nsk doctor` and `UiSelfTestRunner`.
