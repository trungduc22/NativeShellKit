# NativeShellKit Ecosystem Checkpoint

Durable progress log for the 5-repository Windows native ecosystem consolidation (`NativeShellKit`, `HiberDesk`, `GhostCode`, `BackThread`, `ed-local-ai`).

## Step 0 — Audit Findings (Verified)

| Check | Result | Notes |
| --- | --- | --- |
| Execution environment | Linux VM (`dotnet` not installed; WinUI 3 / WindowsAppSDK requires Windows) | All Windows build/test/self-test verification runs via GitHub Actions `windows-latest` CI; local runs are explicitly marked `NOT TESTED`. |
| NSK branch state | `main` = `0.1.0-alpha.1` (`ac5d81a`); `fix/sdk2-worker-drain` = `0.2.0-alpha.3` (already includes `convergence/sdk-2.0` + `feature/ecosystem-integration-contracts` `LocalPeerGrant`) | Merged cleanly into `eco/nativeshellkit-m1` and bumped to `0.2.0-alpha.4`. |
| BackThread pipe ACL | **VERIFIED**: `LocalApi.cs` on `integration/hiberdesk-bridge` sets `UseNamedPipes(options => options.CurrentUserOnly = true)`, embeds current user SID in `BackThread.HiberDesk.v1.<SID>.<port>`, enforces HMAC-derived Bearer token with fixed-time comparison, rejects `Origin`, and enforces route allowlist `HiberDeskApi.Allows`. | Bridge branch is 4 commits behind / 3 ahead of `master` and targets legacy Host/workflow; must be ported onto `BackThread.Daemon` on `master`, NOT direct-merged. |
| BackThread `master` pre-existing CI failure | **OBSERVED**: CI run `44688485949` failed in `macos-ios` on `src/iOS/UI/BTNowPlayingView.m:139` (`showsRouteButton` deprecated under `-Werror,-Wdeprecated-declarations` on Xcode 26.6). | Pre-existing iOS deprecation warning on `master`, unrelated to Windows/NSK. |
| `artifacts/m12/sdk-feed` committed? | **VERIFIED**: Not committed in `GhostCode` or `HiberDesk` (`convergence/*` workflows check out NSK by SHA and pack locally into `artifacts/m12/sdk-feed`). | To be replaced by GitHub Packages feed + CPM in M2. |
| Dependabot NuGet exact-range prerelease | Exact bracket notation `[0.2.0-alpha.4]` is unnecessary under Central Package Management (`Directory.Packages.props`) and can interfere with Dependabot NuGet updates. | Use unbracketed versions (`Version="0.2.0-alpha.4"`) in `Directory.Packages.props`. |
| `ECOSYSTEM_ROADMAP.md` BackThread note | Outdated (claimed BackThread does not use NSK). | Actual rule: `BackThread.Host.Core` (USB/iOS/BTH1/WASAPI audio) MUST remain 100% NSK/WinUI-free; `BackThread.Daemon` and `BackThread.App` consume NSK. |
| `ed-local-ai` runtime baseline | `.NET 8` + `Microsoft.WindowsAppSDK 1.8.260921001` (`winui3` + `practice-fast`, 235/235 backend tests passing on `main`). | NSK targets `net10.0-windows10.0.19041.0`; M6 must not disturb existing .NET 8 automation unless verified safe. |

## Milestone Status

| Milestone | Scope | Branch | Commit / PR | Verification Status |
| --- | --- | --- | --- | --- |
| **M1** | NSK consolidation → `0.2.0-alpha.4` + `ecosystem.capabilities` + `EnablePackageValidation` + `publish-packages.yml` | `eco/nativeshellkit-m1` | Pending push & PR | Local: `NOT TESTED` (Linux VM). CI: Pending GitHub Actions `windows-latest`. Publish/tag `v0.2.0-alpha.4`: **BLOCKED on maintainer authorization**. |
| **M2** | Consumer CPM + GitHub Packages feed (4 apps) | Pending | Pending | Blocked on M1 package publication to GitHub Packages. |
| **M3** | Dependabot + CI compatibility gates + PATCH auto-merge | Pending | Pending | Pending M2. |
| **M4** | GhostCode + HiberDesk peer convergence & `ecosystem.capabilities` | Pending | Pending | Pending M2. |
| **M5** | BackThread HiberDesk bridge ported to `BackThread.Daemon` | Pending | Pending | Pending M2. |
| **M6** | ED-Local-AI minimal opt-in peer & 4-app coexistence report | Pending | Pending | Pending M2–M5. |

## Open Risks & Blockers

1. **Maintainer approval required** before creating tag `v0.2.0-alpha.4` and publishing immutable `.nupkg` packages to GitHub Packages (`https://nuget.pkg.github.com/trungduc22/index.json`).
2. **Pre-existing BackThread `master` iOS CI failure** (`BTNowPlayingView.m:139` `-Werror,-Wdeprecated-declarations`) will keep the 3-OS BackThread CI workflow red until addressed or confirmed out of scope.
3. **`ed-local-ai` `.NET 8` vs NSK `.NET 10`**: Direct NuGet consumption of NSK `net10.0-windows` requires either migrating `ed-local-ai` to `.NET 10` (high risk to existing automation) or implementing a zero-dependency same-user named pipe peer matching the v1 `ecosystem.capabilities` contract.
