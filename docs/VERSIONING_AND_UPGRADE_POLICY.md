# NativeShellKit Versioning and Upgrade Policy

## M12 package train

`0.1.0-alpha.2` identifies the M11 scoped DI and notification contract changes. Existing `alpha.1` feeds remain immutable. See [M12 state](migrations/M12_STATE.md) for validation.

## 1. Semantic Versioning Contract

NativeShellKit follows Semantic Versioning 2.0 (`MAJOR.MINOR.PATCH[-prerelease]`):

- **`0.1.0-alpha.1` (Current Baseline):** Initial unified SDK pre-release targeting `net10.0-windows10.0.19041.0` and `Microsoft.WindowsAppSDK` `2.6.4`. Minor API refinements may occur prior to `0.1.0` / `1.0.0`.
- **`MAJOR` increments:** Reserved for breaking public contract changes in `NativeShellKit.Abstractions` or IPC wire incompatibility.
- **`MINOR` increments:** Backward-compatible features, additional `DesignSystem` controls, optional service extensions, and template improvements.
- **`PATCH` increments:** Bug fixes, security patches, and Windows App SDK servicing bumps without contract breakage.

## 2. Synchronized Package Train

All 13 NuGet packages share a single synchronized version from `Directory.Build.props` (`0.1.0-alpha.1`):

- Foundation & Host: `NativeShellKit.Abstractions`, `NativeShellKit.Hosting`
- Platform & UI: `NativeShellKit.Windows`, `NativeShellKit.WinUI`, `NativeShellKit.DesignSystem`
- Shell Capabilities: `NativeShellKit.Notifications`, `NativeShellKit.Tray`, `NativeShellKit.Security`, `NativeShellKit.Communication`, `NativeShellKit.Diagnostics`
- Packaging & Tooling: `NativeShellKit.Build`, `NativeShellKit.DevTools`, `NativeShellKit.Templates`

Consumer applications should pin matching `NativeShellKit.*` versions so transitive abstractions and WinUI `.xbf`/`.pri` build targets remain aligned.

## 3. Side-by-Side Multi-App Isolation

Installing or upgrading NativeShellKit packages in one application must never disturb another NativeShellKit application on the same machine:

1. Each application defines a distinct `AppIdentity(appId, displayName, publisher, version)`.
2. `AppIdentity` isolates settings, logs, notification history, DPAPI secrets, Win32 Mutexes, named pipes, Startup Run keys, tray GUIDs, and installer GUIDs under `<publisher>\<appId>`.
3. Applications are deployed unpackaged (`WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`) or per-app, avoiding shared machine-wide DLL conflicts.

## 4. IPC Protocol Versioning

- Over-the-wire IPC uses `IpcProtocolVersion.Current` (`"1.0"`).
- Every `IpcEnvelope` carries `protocolVersion`, `appId`, `timestampUnixMs`, `nonce`, and HMAC-SHA256 `signatureHex` derived from a per-application DPAPI-protected key (`IpcAuthManager`).
- Unknown protocol versions return a signed `UNSUPPORTED_PROTOCOL` error rather than attempting partial deserialization.

## 5. Developer Kit Upgrade & Rollback

### Upgrade

Run the setup executable shipped in the target release:

```powershell
.\NativeShellKit-DeveloperKit-Setup-x64.exe --upgrade
.\NativeShellKit-DeveloperKit-Setup-x64.exe --verify
```

`--upgrade` cleanly replaces managed SDK subdirectories (`packages/`, `tools/`, `templates/`, `samples/`, `docs/`) and refreshes `install-manifest.json` + SHA-256 checksums without touching any user application data under `%LOCALAPPDATA%\NativeShellKit`.

### Rollback

1. Re-run `--upgrade` using the previous release's `NativeShellKit-DeveloperKit-Setup-x64.exe` and payload, or
2. Pin the prior package versions in the consumer application's `.csproj` and restore from the archived local feed.
