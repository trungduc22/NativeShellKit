# Pre-Migration Compatibility Matrix (`0.1.0-alpha.1`)

Comparison of the target ecosystem repositories (**GhostCode**, **HiberDesk**, **ED-Local-AI**, and **BackThread**) against **NativeShellKit** `0.1.0-alpha.1` prior to performing application migrations.

## Framework, Package & Packaging Comparison

| Dimension | NativeShellKit (`0.1.0-alpha.1`) | GhostCode (`main`) | HiberDesk (`main`) | ED-Local-AI (`ED-Local-AI-WinUI3`) | BackThread |
| --- | --- | --- | --- | --- | --- |
| **UI Project TFM** | `net10.0-windows10.0.19041.0` | `net10.0-windows10.0.19041.0` | `net10.0-windows10.0.19041.0` | `net8.0-windows10.0.19041.0` *(upgrade needed)* | `net8.0-windows10.0.19041.0` *(non-WinUI)* |
| **Core Library TFM** | `net10.0-windows10.0.19041.0` | `net10.0` (`GhostCode.Core`) *(see TFM note below)* | `net10.0` (`HiberDesk.Core`) *(see TFM note below)* | Single WinUI executable | Non-WinUI host + C/iOS components |
| **Windows App SDK** | `2.6.4` | `2.6.5` *(compatible upward unification)* | `2.6.5` *(compatible upward unification)* | `1.x` baseline *(upgrade to `>= 2.6.4`)* | None (WinForms tray; must NOT force WinUI) |
| **Deployment Model** | `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, x64 | `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, x64, `DISABLE_XAML_GENERATED_MAIN` | `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, x64, `DISABLE_XAML_GENERATED_MAIN` | `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, `DISABLE_XAML_GENERATED_MAIN` | Non-WinUI deployment |
| **WinUI `.xbf` / `.pri` Packaging** | Automatic via `buildTransitive\<PackageId>.targets` inside `NativeShellKit.WinUI` & `DesignSystem` | Manual `IncludeCompiledXamlInPublish` target in `.csproj` | Manual `IncludeCompiledXamlInPublish` target in `.csproj` | Manual build config | N/A |
| **DI Container & Lifetimes** | `ServiceContainer` (`Singleton`, `Scoped`, `Transient`) + `Microsoft.Extensions.DependencyInjection` (`10.0.0`) interop | `Microsoft.Extensions.DependencyInjection` (`11.0.0`) | `Microsoft.Extensions.DependencyInjection` (`11.0.0`) | Custom / manual wiring in `Program.Main` | Custom |
| **Logging & Diagnostics** | `RedactingFileLogger` (`ISdkLogger`), `DiagnosticsService`, `UiSelfTestRunner`, `WindowScreenshotCapture` | `Microsoft.Extensions.Logging` (`11.0.0`), custom `UiSelfTestRunner` | `Microsoft.Extensions.Logging` (`11.0.0`), `PerformanceProbe` | `--self-test` harness | Custom diagnostics |
| **MVVM & Controls** | `ObservableObjectBase`, `RelayCommand`, `DesignSystem` (`PageHeaderControl`, `SettingsSectionControl`, etc.) | `CommunityToolkit.Mvvm` (`8.4.0`), `CommunityToolkit.WinUI.Controls.SettingsControls` (`8.2.251219`), `GhostThemePalette` | `CommunityToolkit.Mvvm` (`8.4.0`), `CommunityToolkit.WinUI.Controls.SettingsControls` (`8.2.251219`) | Custom controls | WinForms controls |
| **DPAPI / Secrets** | `DpapiSecretStore` (`ProtectedData 10.0.0` + `AppUserModelId` entropy) + `SafePathValidator` + `SecretRedactor` | `System.Security.Cryptography.ProtectedData` (`11.0.0`), `Microsoft.Data.Sqlite` (`11.0.0`) | `System.Security.Cryptography.ProtectedData` (`11.0.0`), `Microsoft.Data.Sqlite` (`11.0.0`) | Manual `ProtectedData` auth helper | Local secret store |
| **App Identity Isolation** | `AppIdentity("ghostcode" / "hiberdesk" / ...)` -> isolated dirs, Mutex, IPC/activation pipes, tray GUID, Startup key | Custom paths & `.iss` installer | `SingleInstanceCoordinator`, `WindowsAutoStartService`, `.iss` installer | Manual `Mutex` in `Program.Main` | Independent |
| **Notifications & Tray** | `UnifiedNotificationService` (submission vs suppression vs registration status + supervised async activation) & `NativeTrayService` | `AppNotificationManager` + `Shell_NotifyIconW` | `TrayIconManager` | Limited / manual | WinForms `NotifyIcon` |

## Identified Pre-Migration Differences & Mitigation Actions

1. **Core Class Library TFM (`net10.0` vs `net10.0-windows10.0.19041.0`):**
   - **Finding:** `GhostCode.Core` and `HiberDesk.Core` target pure `net10.0`, while NativeShellKit `0.1.0-alpha.1` libraries target `net10.0-windows10.0.19041.0`.
   - **Impact:** `GhostCode.App` and `HiberDesk.App` (which target `net10.0-windows10.0.19041.0`) can consume every `NativeShellKit.*` package immediately, whereas pure `net10.0` `*.Core` projects cannot directly reference `net10.0-windows10.0.19041.0` packages without either multi-targeting `NativeShellKit.Abstractions` (`net10.0;net10.0-windows10.0.19041.0`) in a follow-up `0.1.0` release or keeping shell/Windows registrations at the `*.App` composition root.
   - **Mitigation:** Keep pure domain logic in `GhostCode.Core` / `HiberDesk.Core` untouched during Stage 1 adoption and wire `NativeShellKit.*` at the `*.App` / `NativeHost` composition layer.

2. **Dependency Version Alignment (`10.0.0` vs `11.0.0` & `2.6.4` vs `2.6.5`):**
   - **Finding:** `GhostCode` and `HiberDesk` reference preview `11.0.0` (`Microsoft.Extensions.DependencyInjection`, `ProtectedData`) and `Microsoft.WindowsAppSDK` `2.6.5`, whereas NativeShellKit targets `10.0.0` and `2.6.4`.
   - **Impact:** NuGet minimal-version resolution (`>= 10.0.0`, `>= 2.6.4`) rolls forward cleanly to `11.0.0` and `2.6.5` in downstream applications without diamond conflicts, provided NativeShellKit packages are consumed as compiled NuGet packages rather than pinned lower bounds.

3. **`Microsoft.Extensions.DependencyInjection` Integration:**
   - **Finding:** Both `GhostCode` and `HiberDesk` register services with `IServiceCollection` and resolve scoped services via `IServiceScopeFactory`.
   - **Mitigation (Implemented in M11):** `NativeShellKit.Hosting` now implements `ServiceLifetimeKind.Scoped`, `IServiceScopeFactory` (`Microsoft.Extensions.DependencyInjection.Abstractions` `10.0.0`), and `ConfigureServices(IServiceCollection)` / `PopulateFrom(IServiceCollection)` extensions so existing DI modules plug directly into `NativeShellHostBuilder`.

4. **WinUI XAML Packaging (`IncludeCompiledXamlInPublish`):**
   - **Finding:** Both `GhostCode.App.csproj` and `HiberDesk.App.csproj` carry custom `IncludeCompiledXamlInPublish` MSBuild targets to ensure `.xbf` and `.pri` files survive unpackaged builds/publishing.
   - **Mitigation:** Keep `IncludeCompiledXamlInPublish` in app `.csproj`s for app-local views; NativeShellKit's own `NativeShellKit.WinUI` and `NativeShellKit.DesignSystem` NuGet packages automatically deploy their library `.xbf` assets via `buildTransitive\<PackageId>.targets`.

5. **Developer Kit Independence for End Users:**
   - All three WinUI target apps ship unpackaged (`WindowsAppSDKSelfContained=true`) or per-app installers; **no end-user machine needs the NativeShellKit Developer Kit installed**.
