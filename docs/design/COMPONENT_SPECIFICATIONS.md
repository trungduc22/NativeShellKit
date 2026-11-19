# NativeShellKit 2.0 — Component Specifications

**Status:** Approved Implementation Blueprint (`docs/architecture-2.0`)  
**Packages:** `NativeShellKit.WinUI` & `NativeShellKit.DesignSystem` (`0.2.0-alpha.2`)

---

## 1. Universal Control Engineering Standards

Every control shipped in `NativeShellKit.WinUI` and `NativeShellKit.DesignSystem` **MUST** obey all six rules before merging:

1. **100% `DependencyProperty`-Backed API:** Every bindable property (`Title`, `Subtitle`, `State`, `Severity`, `Label`, `Command`, etc.) MUST be a registered `DependencyProperty` (never a bare CLR property mutating `x:Name` fields directly) so WinUI `{x:Bind}` and `{Binding}` work reliably.
2. **Templated Controls for Primitives:** Leaf/reusable primitives (`StatusBadge`, `MetricCard`, `PageHeader`, `SettingsRow`, `SettingsSection`, `StateView`, `ProgressBanner`, `CompactMediaBar`, `ModuleCardBoundary`) MUST inherit `Microsoft.UI.Xaml.Controls.Control` with `DefaultStyleKey = typeof(TControl)` defined in `Themes/Generic.xaml`, enabling consumer style/template customization without forking controls.
3. **Zero Hardcoded Colors in C#:** All foreground, background, and border brushes MUST resolve from `{ThemeResource Nsk*}` or standard WinUI `{ThemeResource *}` via `VisualStateManager.GoToState(...)`.
4. **Zero Hardcoded Locale Copy:** Never hardcode English strings (`"Retry"`, `"Toggle theme"`, `"Close"`) inside controls. All user-visible strings MUST be `DependencyProperty` inputs with neutral defaults (supporting HiberDesk/GhostCode/ED-Local-AI Vietnamese + English localization seamlessly).
5. **Deterministic Event Cleanup:** Any control subscribing to singleton services (`INavigationService`, `IThemeService`, `INotificationService`, `XamlRoot.Changed`) MUST unsubscribe in `Unloaded` or `Dispose()`.
6. **Built-In WinUI Controls First:** Wrap and style native WinUI 3 controls (`NavigationView`, `InfoBar`, `AutoSuggestBox`, `ContentDialog`, `ProgressBar`, `ProgressRing`, `Expander`, `SelectorBar`, `Slider`) rather than reinventing input handling, IME, or accessibility peers.

---

## 2. Shell & Navigation Components (`NativeShellKit.WinUI`)

### 2.1 `NativeShellFrame` (Replaces v1 `NativeShellView`)

**Purpose:** Single unified desktop shell host supporting all five `ShellMode` variants (`Dashboard`, `ModuleHost`, `CompactUtility`, `SinglePage`, `TrayFirst`).

| Member | Type | Default / Behavior |
| --- | --- | --- |
| `Mode` (DP) | `ShellMode` | `ShellMode.Dashboard` (`Dashboard`, `ModuleHost`, `CompactUtility`, `SinglePage`) |
| `Density` (DP) | `ShellDensity` | `ShellDensity.Standard` (`Standard` or `Compact` — swaps merged density tokens) |
| `AppTitle` (DP) | `string` | Bound to `AppIdentity.DisplayName` |
| `AppSubtitle` (DP) | `string?` | Optional route/workspace subtitle |
| `AppIconSource` (DP) | `ImageSource?` | Optional app logo (falls back to `AppIconGlyph`) |
| `TitleBarSearchContent` (DP) | `object?` | Centered title bar slot (auto-registered for Win32 `Passthrough` hit-testing) |
| `TitleBarStatusContent` (DP) | `object?` | Right-side status badge/sync pill slot |
| `TitleBarActionsContent` (DP) | `object?` | Right-side buttons (theme toggle, quick actions — auto-passthrough) |
| `FooterStripContent` (DP) | `object?` | Optional persistent bottom bar (e.g., `CompactMediaBar` or wizard action bar) |
| `ShowBackButton` (DP) | `bool` | `true` in `Dashboard`/`ModuleHost`, `false` in `CompactUtility`/`SinglePage` |
| `AttachServices(...)` | Method | Wires `INavigationService`, `IThemeService`, `INotificationService`, `IShellWindowService`, `IUiDispatcher`; returns `IDisposable` subscription handle (auto-disposed on `Unloaded`) |

**Navigation Caching & Lifecycle (`INavigationAware`):**
```csharp
public interface INavigationAware
{
    void OnNavigatedTo(object? parameter);
    void OnNavigatedFrom();
}
```
- Routes register a `NavigationCachePolicy` (`Cached` default for top-level shell routes; `Transient` for parameterized detail pages).
- Cached pages stay instantiated in `NativeShellFrame`'s dictionary (eliminating HiberDesk's current page-rebuild-per-click overhead) and receive `OnNavigatedFrom()` / `OnNavigatedTo(parameter)`.
- `ShellNavigationView` automatically adapts `PaneDisplayMode`: `Left` (`>= 1008px`), `LeftCompact` (`641–1007px`), `LeftMinimal` (`<= 640px`).
- `CompactUtility` hides `NavigationView` and renders a slim top `SelectorBar` populated from the same `INavigationService.RegisteredRoutes`!

### 2.2 `ShellTitleBar` (Replaces v1 `CustomTitleBarControl`)

**Purpose:** Native WinUI 3 custom title bar with automatic caption-button inset reservation and DPI-accurate Win32 non-client pointer passthrough.

| Member | Type | Behavior |
| --- | --- | --- |
| `Title` / `Subtitle` (DP) | `string` | Left-aligned brand block; `Subtitle` uses `NskTextSecondaryBrush` |
| `IconSource` / `IconGlyph` (DP) | `ImageSource?` / `string?` | Supports app `.png` logo (like HiberDesk/GhostCode) or Segoe Fluent glyph |
| `CenterContent` (DP) | `object?` | Search box / route switcher slot |
| `RightContent` (DP) | `object?` | Status pill + custom actions (no hardcoded theme button — apps opt in via `ShellTitleBarThemeButton` or custom button) |
| `Compact` (DP) | `bool` | `false` (`44px` height) vs `true` (`36px` height for `CompactUtility`) |
| `AttachWindow(Window)` | Method | Subscribes `Loaded`, `SizeChanged`, `XamlRoot.Changed`, `ActualThemeChanged`; computes `RightInset / RasterizationScale` and registers physical rects of all interactive children inside `CenterContent` and `RightContent` via `InputNonClientPointerSource.SetRegionRects(NonClientRegionKind.Passthrough, ...)` |

---

## 3. Core Design System Controls (`NativeShellKit.DesignSystem`)

### 3.1 `StatusBadge` (Replaces v1 `StatusIndicatorControl`)

**Visual States (`BadgeState`):** `Healthy`, `Active`, `Warning`, `Error`, `Offline`, `Syncing`.  
**Fixes v1 defect:** Uses `{ThemeResource NskStatus*BgBrush}` / `{ThemeResource NskStatus*FgBrush}` via `VisualStateManager` (zero `Color.FromArgb` in C#), plus `1px` border in `HighContrast` and an animated `ProgressRing` (`10x10`) in `Syncing` state (respecting reduced motion).

| Property (DP) | Type | Default |
| --- | --- | --- |
| `State` | `ShellStatusKind` | `ShellStatusKind.Healthy` |
| `Label` | `string` | `""` |
| `Compact` | `bool` | `false` (dot-only + tooltip when compact) |
| `ShowDot` | `bool` | `true` |

**Accessibility:** `AutomationProperties.Name` auto-formats `"{Label} ({State})"` unless explicitly overridden; `LiveSetting="Polite"` optional via `AnnounceChanges` DP.

### 3.2 `PageHeader` (Upgrades v1 `PageHeaderControl`)

| Property (DP) | Type | Behavior |
| --- | --- | --- |
| `Title` | `string` | `NskPageTitleTextStyle`, `AutomationProperties.HeadingLevel="Level1"` |
| `Subtitle` | `string?` | `NskBodyTextStyle`, `NskTextSecondaryBrush`, collapsed when null/empty |
| `BreadcrumbItems` | `IReadOnlyList<string>?` | Optional `BreadcrumbBar` above title for nested detail views |
| `BadgeState` / `BadgeLabel` | `ShellStatusKind?` / `string?` | Renders inline `StatusBadge` next to title |
| `ActionsContent` | `object?` | Right-aligned action bar; automatically wraps below title below `640px` breakpoint |

### 3.3 `MetricCard` & `MetricStrip` (Upgrades v1 `CompactMetricCard`)

| Property (DP) | Type | Behavior |
| --- | --- | --- |
| `Label` | `string` | `NskCaptionTextStyle`, `NskTextSecondaryBrush` |
| `Value` | `string` | `NskDisplayMetricTextStyle` with tabular numerals |
| `Caption` | `string?` | Secondary delta/context line |
| `Glyph` | `string?` | Optional Segoe Fluent icon |
| `TrendState` | `ShellStatusKind?` | Optional semantic color accent on caption/delta |
| `IsLoading` | `bool` | Renders shimmer/placeholder bar (static subtle surface when reduced motion enabled) |
| `Command` / `CommandParameter` | `ICommand?` / `object?` | Makes card interactive (`Normal` / `PointerOver` / `Pressed` / `Focused` states) when non-null |

`MetricStrip` is a responsive wrapping `Panel`/`ItemsRepeater` layout distributing `MetricCard` children evenly (4 columns >= 1008px, 2 columns >= 641px, 1 column <= 640px).

### 3.4 `StateView` (Upgrades v1 `StateViewControl`)

**States (`ShellViewState`):** `Content`, `Loading`, `Skeleton`, `Empty`, `Error`, `Offline`, `Degraded`.  
**Attributes:** `[ContentProperty(Name = nameof(BodyContent))]`.

| Property (DP) | Type | Behavior |
| --- | --- | --- |
| `BodyContent` | `object?` | Primary content shown in `Content` (and optionally dimmed/bannered in `Degraded`) |
| `State` | `ShellViewState` | Drives `VisualStateManager` group `ViewStates` |
| `Title` / `Message` | `string` | Localizable state heading & explanation |
| `IconGlyph` | `string?` | Overrides default semantic glyph (`Error` `\uEA39`, `Empty` `\uE8F4`, `Offline` `\uF384`, `Degraded` `\uE7BA`) |
| `PrimaryActionLabel` / `PrimaryActionCommand` | `string?` / `ICommand?` | Replaces hardcoded `"Retry"` button; collapsed when null/empty |
| `SecondaryActionLabel` / `SecondaryActionCommand` | `string?` / `ICommand?` | Optional secondary recovery (e.g., "Open Settings" / "View Logs") |
| `SkeletonTemplate` | `DataTemplate?` | Custom skeleton placeholder in `Skeleton` state |

### 3.5 `NotificationBanner` & `NotificationBannerStack` (Upgrades v1 `NotificationBannerControl`)

- Wraps native WinUI `InfoBar` styled with `NskRadiusCard` and semantic tokens.
- Supports `DependencyProperty` binding to `NotificationRecord` OR standalone `Title` / `Message` / `Severity` / `ActionLabel` / `ActionCommand` / `IsOpen`.
- **`NotificationBannerStack`** manages up to 2 simultaneously visible banners (coalescing older low-severity info banners into a quiet badge counter so bursts never push page content off-screen), auto-dismisses non-error informational banners after configurable `AutoDismissDuration` (default `6s`, paused on pointer hover or keyboard focus!), and sets `AutomationProperties.LiveSetting="Assertive"` for `Error` and `"Polite"` for `Info`/`Success`/`Warning`.

### 3.6 `SettingsSection` & `SettingsRow` (Upgrades v1 `SettingsSectionControl` / `SettingsRowControl`)

- `SettingsSection`: `[ContentProperty(Name = nameof(Items))]`, `Title` (DP, `HeadingLevel="Level2"`), `Description` (DP, `NskTextSecondaryBrush`), `HeaderActionContent` (DP).
- `SettingsRow`: `[ContentProperty(Name = nameof(ActionContent))]`, `Header` (DP), `Description` (DP), `Glyph` (DP), `ActionContent` (DP), `ValidationMessage` (DP, inline warning/error below description), `IsExpandable` (DP — renders native `Expander` variant with `ExpandedContent` DP for nested settings like BackThread/OAuth configuration!).
- **Migration Benefit:** Gives HiberDesk and GhostCode a first-party, theme-consistent alternative to `CommunityToolkit.WinUI.Controls.SettingsControls` with bilingual accessibility and validation support.

### 3.7 `SearchBox` (Upgrades v1 `SearchBarControl`)

- Wraps native WinUI `AutoSuggestBox` with built-in **configurable debounce** (`DebounceMilliseconds` DP, default `200ms`, properly cancelling & disposing `CancellationTokenSource` on `Unloaded` — fixing the leak pattern GhostCode had to patch in `54e96bb`!).
- Supports `Placeholder` (DP), `QueryText` (TwoWay DP), `ShortcutHint` (DP, e.g. `"Ctrl+K"` displayed as muted pill when unfocused and empty), `ItemsSource` (DP), `QuerySubmittedCommand` (DP), and `FocusAndSelectAll()`.

### 3.8 `CompactMediaBar` (NEW — Serves HiberDesk BackThread Companion & Media Hubs)

**Purpose:** Native Fluent 2 compact media & audio routing control usable both as a standalone card (`DevicesPage` / `CompanionPanel`) and as a slim docked footer bar (`NativeShellFrame.FooterStripContent`). Zero BackThread SDK coupling — binds to pure UI properties & `ICommand`s!

| Property / Command (DP) | Type | Behavior |
| --- | --- | --- |
| `LayoutMode` | `MediaBarLayout` | `Card` (full companion card with seek + route selector + volume flyout) or `Strip` (single-row `56px` docked mini-bar) |
| `ConnectionState` | `ShellStatusKind` | Renders `StatusBadge` (`Online`, `Connecting`, `Offline`, `Warning`) |
| `Title` / `Artist` / `ProviderLabel` | `string` | Truncated with `TextTrimming="CharacterEllipsis"` + full tooltip |
| `ArtworkSource` | `ImageSource?` | Optional `40x40` rounded thumbnail (`NskRadiusControl`); falls back to music glyph `\uE8D6` |
| `IsPlaying` | `bool` | Toggles Play (`\uE768`) / Pause (`\uE769`) icon & `AutomationProperties.Name` |
| `PositionSeconds` / `DurationSeconds` / `BufferedSeconds` | `double` | Drives native `Slider` scrubber + tabular `mm:ss / mm:ss` labels; disabled when `CanSeek == false` |
| `RouteLabel` | `string` | Active output badge (`PC`, `iPhone`, `Both`) |
| `VolumePercent` / `IsMuted` | `double` / `bool` | Inline mute button + volume slider flyout (`Flyout` with PC / App / Player layer sliders when expanded) |
| `PlayPauseCommand`, `PreviousCommand`, `NextCommand`, `SeekCommand`, `HandoffCommand`, `ReturnToPcCommand` | `ICommand?` | Bound to ViewModel commands (`CompanionViewModel`); buttons auto-disable when `CanExecute == false` |

**Accessibility:** Every icon-only transport button requires localized `AutomationProperties.Name` DPs (`PlayLabel`, `PauseLabel`, `PreviousLabel`, `NextLabel`, `SeekSliderLabel`), supporting HiberDesk's bilingual En/Vi labels.

### 3.9 `ProgressBanner` & `InlineProgressIndicator` (NEW)

- **`ProgressBanner`**: Compact card/strip for active operations (cloud sync, archive upload, file organization, ED unit scan) featuring `Title`, `StatusCaption`, `ProgressPercent` (`double?` — indeterminate `ProgressBar` when `null`, determinate `0..100` when set), `IsPaused`, and optional `PauseResumeCommand` + `CancelCommand`.
- **`InlineProgressIndicator`**: Slim `16px` `ProgressRing` + caption for toolbar/header busy feedback without blocking page interaction.

### 3.10 `DialogCoordinator` 2.0 (`IDialogService` in `NativeShellKit.WinUI`)

Upgrades v1 `DialogCoordinator` (`src/NativeShellKit.WinUI/Dialogs/DialogCoordinator.cs`):
- Serializes concurrent dialog requests cleanly via `SemaphoreSlim(1, 1)` AND marshals onto `IUiDispatcher` (fixing v1 bug where calling `ShowMessageAsync` from a background thread crashed WinUI `ContentDialog`!).
- Supports structured dialog variants:
  1. `ShowMessageAsync(DialogRequest)` — Informational / Confirmation with `DefaultButton` (`Primary`, `Secondary`, `Close`).
  2. `ShowDestructiveConfirmAsync(DestructiveDialogRequest)` — Applies danger styling to primary action (`NskStatusDangerFgBrush`) and defaults focus to `Close` button to prevent accidental Enter confirmation (e.g., GhostCode permanent trash purge, HiberDesk conflict override).
  3. `ShowProgressAsync(ProgressDialogRequest, Func<IProgress<DialogProgressUpdate>, CancellationToken, Task>)` — Non-dismissible or cancelable Fluent progress modal with live status updates.
  4. `ShowFormAsync<TResult>(FormDialogRequest<TResult>)` — Typed validated input dialog (replaces ad-hoc `ContentDialog` builders in `CompanionPanel` Setup dialog & `GhostCode` dialogs).

### 3.11 `ModuleCardBoundary` (NEW — Error-Isolated Module UI Host)

**Purpose:** Wraps any pluggable module UI widget (such as HiberDesk's Overview Ecosystem Card or `CompanionPanel`) so rendering exceptions, `ModuleHealthState.Faulted`, `Disabled`, or `Degraded` states render cleanly inside a Fluent `StateView` card without crashing the parent page or `MainWindow`.

| Property (DP) | Type | Behavior |
| --- | --- | --- |
| `ModuleId` / `ModuleTitle` | `string` | Identifies source module for diagnostics & header |
| `HealthState` | `ModuleHealthState` | `Healthy` renders `ModuleContent`; `Disabled` collapses or shows opt-in prompt; `Degraded`/`Faulted` shows recovery card with `RetryCommand` & redacted diagnostic summary |
| `ModuleContent` | `object?` | Wrapped module UI |
| `RetryCommand` / `ConfigureCommand` | `ICommand?` | Recovery/setup actions |

---

## 4. v1 -> v2 Component Migration Map

| v1 Control (`0.1.0-alpha.2`) | v2 Status (`0.2.0-alpha.2`) | Breaking Change Policy |
| --- | --- | --- |
| `NativeShellView` | Replaced by `NativeShellFrame` | Keep `NativeShellView` as `[Obsolete]` thin wrapper delegating to `NativeShellFrame(Mode=Dashboard)` for 1 minor version (`0.2.x`) |
| `CustomTitleBarControl` | Replaced by `ShellTitleBar` | Keep `CustomTitleBarControl` as `[Obsolete]` wrapper through `0.2.x` |
| `StatusIndicatorControl` | Replaced by `StatusBadge` | Keep `StatusIndicatorControl` forwarding to `StatusBadge` (removing hardcoded `Color.FromArgb`) |
| `PageHeaderControl` | Upgraded in-place (or aliased to `PageHeader`) | Convert all CLR properties to `DependencyProperty` (source-compatible!) |
| `CompactMetricCard` | Upgraded in-place (or aliased to `MetricCard`) | Convert CLR properties to `DependencyProperty` + add `IsLoading`, `Command` |
| `StateViewControl` | Upgraded in-place (`StateView`) | Convert to `DependencyProperty` + keep `SetState(...)` helper method for backward compatibility |
| `NotificationBannerControl` | Upgraded in-place (`NotificationBanner`) | Convert to `DependencyProperty` + keep `Show(NotificationRecord, string?)` method |
| `SettingsSectionControl` / `SettingsRowControl` | Upgraded in-place (`SettingsSection` / `SettingsRow`) | Convert to `DependencyProperty` + add `[ContentProperty]` + expandable mode |
| `SearchBarControl` | Upgraded in-place (`SearchBox`) | Convert to `DependencyProperty` + add built-in debounce (`DebounceMilliseconds`) |
| *(New)* | `CompactMediaBar`, `ProgressBanner`, `InlineProgressIndicator`, `MetricStrip`, `NotificationBannerStack`, `ModuleCardBoundary` | Additive in `0.2.0-alpha.2` |
