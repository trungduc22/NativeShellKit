# NativeShellKit Ecosystem Checkpoint

Durable progress log for the 5-repository Windows native ecosystem consolidation (`NativeShellKit`, `HiberDesk`, `GhostCode`, `BackThread`, `ed-local-ai`).

## Step 0 — Audit Findings (Verified)

| Check | Result | Notes |
| --- | --- | --- |
| Execution environment | Linux VM (`dotnet` not installed; WinUI 3 / WindowsAppSDK requires Windows) | All local Windows builds/tests are explicitly marked `NOT TESTED`; all verification below is backed by real GitHub Actions `windows-latest` / `ubuntu-latest` / `macos-latest` CI runs. |
| NSK branch state | `main` consolidated `fix/sdk2-worker-drain` + `convergence/sdk-2.0` + `feature/ecosystem-integration-contracts` + `ac5d81a` into `0.2.0-alpha.4` | Published 13 immutable `.nupkg` packages to GitHub Packages (`https://nuget.pkg.github.com/trungduc22/index.json`). |
| BackThread pipe ACL | **VERIFIED**: `LocalApi.cs` uses `UseNamedPipes(options => options.CurrentUserOnly = true)`, SID-scoped pipe `BackThread.HiberDesk.v1.<SID>.<port>`, HMAC-derived Bearer token with fixed-time comparison, `Origin` rejection, and `HiberDeskApi.Allows` allowlist. | Ported cleanly onto `master` (`BackThread.Host` + `BackThread.Daemon --hiberdesk`) without direct-merging the diverged legacy branch. |
| BackThread `master` pre-existing iOS CI failure | **RESOLVED (maintainer-authorized)**: Replaced deprecated `MPVolumeView.showsRouteButton` with standard iOS 11+ `AVRoutePickerView` in `BTNowPlayingView.m` (preserving iOS 15.0 deployment target) and fixed swapped `write_ipa(ipa_path, app_dir)` args in `ci.yml`. | `macos-ios` CI is now GREEN and produces a verified iOS 15 arm64 Mach-O & `.ipa`. |
| `artifacts/m12/sdk-feed` committed? | **VERIFIED**: Not committed in `GhostCode` or `HiberDesk`; removed local checkout+pack from CI in favor of GitHub Packages + CPM. | Local source override remains available via `-p:NativeShellKitUseSource=true -p:NativeShellKitRoot=...` in BackThread. |
| Dependabot NuGet exact-range prerelease | Used unbracketed versions (`Version="0.2.0-alpha.4"`) inside `Directory.Packages.props` (CPM) for reliable Dependabot grouped updates. | `dependabot.yml` + `sdk-compatibility-gate.yml` configured in all 4 consumer repos (PATCH-only auto-merge; MINOR/MAJOR held for human review). |
| `ed-local-ai` runtime baseline | `.NET 8` + `Microsoft.WindowsAppSDK 1.8.260921001` (`winui3` + `practice-fast`, 235/235 backend tests). | Preserved `.NET 8` runtime & automation untouched; added CPM + Dependabot + SDK gate + default-OFF opt-in `EcosystemPeer.cs` (`ecosystem.capabilities` v1). |

## Final Compatibility Matrix (M1–M6 Complete)

| Repository | Branch / PR | NSK Version | CI Workflows Verified | Local VM |
| --- | --- | --- | --- | --- |
| **NativeShellKit** | `main` (PR #1, #2, #3 merged; tag `v0.2.0-alpha.4`) | `0.2.0-alpha.4` (published to GitHub Packages) | `NativeShellKit CI` (`44693643949`) **SUCCESS**; `Publish NativeShellKit Packages` (`44726417367`) **SUCCESS** (34/34 tests, 3 sample self-tests, Developer Kit, 13 `.nupkg` pushed) | `NOT TESTED` |
| **GhostCode** | `eco/ghostcode-m2-m4` (PR #1) | `0.2.0-alpha.4` (CPM + GitHub Packages) | `SDK Compatibility Gate` (`44727133996`) **SUCCESS**; `NativeShellKit migration checks` (`44727123081`, incl. WinUI M12 GUI self-test) **SUCCESS** | `NOT TESTED` |
| **HiberDesk** | `eco/hiberdesk-m2-m4` (PR #2) | `0.2.0-alpha.4` (CPM + GitHub Packages) | `HiberDesk CI` (`44727144921`, `44727941094`) **SUCCESS**; `SDK Compatibility Gate` (`44727940991`, incl. `BackThreadBridgeTests` capabilities test) **SUCCESS** | `NOT TESTED` |
| **BackThread** | `eco/backthread-m2-m5` (PR #1) | `0.2.0-alpha.4` (Daemon + App only; `Host.Core`/Audio/USB/iOS 100% NSK-free) | `BackThread CI` (`44727946165`: `linux-core`, `windows-desktop`, `macos-ios`) **SUCCESS**; `SDK Compatibility Gate` (`44727946133`, incl. `hiberdesk.bridge-pipe-auth-and-capabilities` & Daemon smoke test) **SUCCESS** | `NOT TESTED` |
| **ed-local-ai** | `eco/ed-local-ai-m2-m6` (PR #1) | `.NET 8` preserved (NuGet feed + CPM + Dependabot ready; default-OFF `EcosystemPeer.cs` v1) | `SDK Compatibility Gate` (`44727218054`: `winui3` + `practice-fast` publish & 235/235 backend tests) **SUCCESS** | `NOT TESTED` |

## Remaining Items & Open Risks

1. **Consumer PR Review & Merge**: `GhostCode` PR #1, `HiberDesk` PR #2, `BackThread` PR #1, and `ed-local-ai` PR #1 are open with green CI and ready for maintainer review/merge into their default branches (`main` / `master`).
2. **Optional `ECOSYSTEM_DISPATCH_TOKEN` Secret**: To enable instant `repository_dispatch` (`nativeshellkit-released`) from `NativeShellKit` on tag pushes (in addition to daily Dependabot checks), add a fine-grained PAT with `contents: write` across the 4 consumer repos as `ECOSYSTEM_DISPATCH_TOKEN` in `NativeShellKit` secrets.
3. **Package Validation Baseline Activation**: Once `0.2.0-alpha.5` is developed, pass `-p:EnablePackageValidationBaseline=true` (or set `<PackageValidationBaselineVersion>0.2.0-alpha.4</PackageValidationBaselineVersion>` in `src/Directory.Build.props`) to enforce binary API compatibility against `0.2.0-alpha.4` on GitHub Packages.
4. **Live 4-App Desktop Coexistence & Dependabot Bump Dry-Run**: End-to-end 4-app interactive coexistence on a real Windows desktop (simultaneous HiberDesk + GhostCode + BackThread Daemon + ED-Local-AI) and the first live Dependabot PR cycle remain `NOT TESTED` until the consumer PRs are merged to their default branches (Dependabot only scans default branches).
5. **`ed-local-ai` `.NET 10` Migration**: Deliberately deferred to protect existing `.NET 8` automation; `ed-local-ai` already participates in `ecosystem.capabilities` v1 via default-OFF `EcosystemPeer.cs`.
