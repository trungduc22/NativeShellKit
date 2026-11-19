using System.Collections.ObjectModel;
using System.Collections.Specialized;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Threading;
using NativeShellKit.DesignSystem.Tokens;
using NativeShellKit.WinUI.Dialogs;
using NativeShellKit.WinUI.Mvvm;
using NativeShellKit.WinUI.Navigation;
using NativeShellKit.WinUI.Shell;
using NativeShellKit.WinUI.Theming;
using Windows.UI;
using Xunit;

namespace NativeShellKit.Tests;

public sealed class UiServicesAndDesignSystemTests
{
    [Fact]
    public void ThemeService_SwitchesModes_ComputesContrastPalettes_AndFiresEvents()
    {
        var theme = new WinUiThemeService(attachSystemListener: false);
        int changedCount = 0;
        theme.ThemeChanged += (_, _) => changedCount++;

        theme.ApplyTheme(AppThemeMode.Dark, "#0F766E");
        Assert.Equal(AppThemeMode.Dark, theme.CurrentMode);
        Assert.True(theme.IsDarkEffective);

        theme.ApplyTheme(AppThemeMode.Light, "#0F766E");
        Assert.Equal(AppThemeMode.Light, theme.CurrentMode);
        Assert.False(theme.IsDarkEffective);

        theme.ApplyTheme(AppThemeMode.System);
        Assert.Equal(AppThemeMode.System, theme.CurrentMode);
        Assert.Equal(3, changedCount);

        foreach (string seed in new[] { "#FFFFFF", "#FACC15", "#0F766E", "#2563EB", "#1E3A8A", "#000000" })
        {
            var pLight = theme.ComputePalette(seed, isDark: false);
            Assert.True(WinUiThemeService.TryParseHex(pLight.PrimaryHex, out Color primary));
            Assert.True(WinUiThemeService.TryParseHex(pLight.ContrastForegroundHex, out Color contrast));
            Assert.True(
                WinUiThemeService.ContrastRatio(primary, contrast) >= 4.5,
                $"Light contrast for {seed} ({pLight.PrimaryHex} on {pLight.ContrastForegroundHex}) was < 4.5");

            var pDark = theme.ComputePalette(seed, isDark: true);
            Assert.True(WinUiThemeService.TryParseHex(pDark.PrimaryHex, out Color darkPrimary));
            Assert.True(WinUiThemeService.TryParseHex(pDark.ContrastForegroundHex, out Color darkContrast));
            Assert.True(
                WinUiThemeService.ContrastRatio(darkPrimary, darkContrast) >= 4.5,
                $"Dark contrast for {seed} ({pDark.PrimaryHex} on {pDark.ContrastForegroundHex}) was < 4.5");
        }
    }

    [Fact]
    public void ThemeService_HighContrastAndReducedMotionFlags_AreRespected()
    {
        var hcTheme = new WinUiThemeService(
            highContrastOverride: () => true,
            animationsEnabledOverride: () => false,
            attachSystemListener: false);

        Assert.True(hcTheme.IsHighContrast);
        Assert.False(hcTheme.IsAnimationsEnabled);

        hcTheme.BrandAccentHex = "#10B981";
        hcTheme.ApplyTheme(AppThemeMode.Dark, AccentSourceMode.AppBrand, backdrop: ShellBackdropKind.MicaAlt);
        Assert.Equal(AccentSourceMode.AppBrand, hcTheme.AccentMode);
        Assert.Equal(ShellBackdropKind.Solid, hcTheme.EffectiveBackdrop);
        Assert.Equal("#10B981", hcTheme.CurrentAccent.PrimaryHex);
    }

    [Fact]
    public void DesignTokens_MaintainConsistentScaleAndSemanticColors()
    {
        Assert.True(DesignTokens.Spacing.XXS < DesignTokens.Spacing.XS);
        Assert.True(DesignTokens.Spacing.XS < DesignTokens.Spacing.S);
        Assert.True(DesignTokens.Spacing.S < DesignTokens.Spacing.M);
        Assert.True(DesignTokens.Spacing.M < DesignTokens.Spacing.L);
        Assert.True(DesignTokens.Spacing.L < DesignTokens.Spacing.XL);

        Assert.True(DesignTokens.Radii.Control < DesignTokens.Radii.Card);
        Assert.True(DesignTokens.Radii.Card < DesignTokens.Radii.Overlay);
        Assert.True(DesignTokens.Typography.CaptionSize < DesignTokens.Typography.BodySize);
        Assert.True(DesignTokens.Typography.SectionTitleSize < DesignTokens.Typography.PageTitleSize);

        Assert.Equal("#2563EB", DesignTokens.SemanticHex.AccentLight);
        Assert.Equal("#60A5FA", DesignTokens.SemanticHex.AccentDark);
        Assert.NotEqual(DesignTokens.SemanticHex.SuccessLight, DesignTokens.SemanticHex.DangerLight);
    }

    [Fact]
    public void NavigationService_OrdersRoutes_ForwardsParameters_AndHandlesBackStack()
    {
        var nav = new WinUiNavigationService();
        nav.RegisterRoute(new NavigationRouteDescriptor("settings", "Settings", "\uE713", () => "settings", Order: 1, IsFooterItem: true));
        nav.RegisterRoute(new NavigationRouteDescriptor("details", "Details", "\uE946", () => "details", Order: 2, CachePolicy: NavigationCachePolicy.Transient));
        nav.RegisterRoute(new NavigationRouteDescriptor("home", "Home", "\uE80F", () => "home", Order: 0, CachePolicy: NavigationCachePolicy.Cached));

        Assert.Equal(["home", "details", "settings"], nav.RegisteredRoutes.Select(r => r.Route).ToArray());
        Assert.Equal(NavigationCachePolicy.Cached, nav.FindRoute("home")?.CachePolicy);
        Assert.Equal(NavigationCachePolicy.Transient, nav.FindRoute("details")?.CachePolicy);

        NavigationEventArgs? lastArgs = null;
        nav.Navigated += (_, e) => lastArgs = e;

        Assert.False(nav.Navigate("unknown-route"));
        Assert.True(nav.Navigate("home"));
        Assert.False(nav.CanGoBack);

        Assert.True(nav.Navigate("details", parameter: 123));
        Assert.Equal("details", nav.CurrentRoute);
        Assert.Equal(123, lastArgs?.Parameter);
        Assert.Equal("home", lastArgs?.PreviousRoute);
        Assert.True(nav.CanGoBack);

        Assert.True(nav.GoBack());
        Assert.Equal("home", nav.CurrentRoute);
        Assert.False(nav.CanGoBack);
        Assert.False(nav.GoBack());
    }

    [Fact]
    public void ObservableCollectionSync_ApplyKeyedDiff_ReconcilesWithoutReset()
    {
        var list = new ObservableCollection<DiffItem>
        {
            new("a", "Alpha", 1),
            new("b", "Beta", 2),
            new("c", "Gamma", 3)
        };

        var actions = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => actions.Add(e.Action);

        var desired = new List<DiffItem>
        {
            new("c", "Gamma Updated", 30),
            new("d", "Delta", 4),
            new("a", "Alpha", 1)
        };

        list.ApplyKeyedDiff(
            desired,
            i => i.Id,
            (l, r) => l.Label == r.Label && l.Rank == r.Rank,
            (existing, next) =>
            {
                existing.Label = next.Label;
                existing.Rank = next.Rank;
            });

        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
        Assert.Equal(["c", "d", "a"], list.Select(i => i.Id).ToArray());
        Assert.Equal("Gamma Updated", list[0].Label);
        Assert.Equal(30, list[0].Rank);
        Assert.Equal("Delta", list[1].Label);
    }

    [Fact]
    public async Task DialogCoordinator_SerializesConcurrentRequests_AndMarshalsOffUiThread()
    {
        var dispatcher = new MockOffThreadDispatcher();
        int active = 0;
        int maxConcurrent = 0;

        var coordinator = new DialogCoordinator(dispatcher, async (inner, ct) =>
        {
            Assert.True(dispatcher.IsOnMockUiThread, "Dialog action did not marshal via IUiDispatcher.");
            int cur = Interlocked.Increment(ref active);
            maxConcurrent = Math.Max(maxConcurrent, cur);
            try
            {
                await Task.Delay(35, ct);
                _ = await inner();
                return DialogOutcome.Primary;
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });

        var t1 = coordinator.ShowMessageAsync(new DialogRequest("One", "First"));
        var t2 = coordinator.ShowDestructiveConfirmAsync(new DestructiveDialogRequest("Two", "Delete?", "Delete"));
        var t3 = coordinator.ShowMessageAsync(new DialogRequest("Three", "Third"));

        var outcomes = await Task.WhenAll(t1, t2, t3);

        Assert.Equal(1, maxConcurrent);
        Assert.True(dispatcher.DispatchCount >= 3);
        Assert.All(outcomes, o => Assert.Equal(DialogOutcome.Primary, o));

        bool progressRan = false;
        await coordinator.ShowProgressAsync(
            new ProgressDialogRequest("Working", "Starting..."),
            async (progress, ct) =>
            {
                progress.Report(new DialogProgressUpdate("Halfway", 50));
                await Task.Delay(10, ct);
                progressRan = true;
            });
        Assert.True(progressRan);
    }

    [Fact]
    public void ShellWindowService_ClampBoundsToWorkArea_KeepWindowVisibleAndMinSized()
    {
        var offscreen = ShellWindowService.ClampBoundsToWorkArea(
            new WindowBounds(-900, -400, 3200, 1800),
            workX: 0,
            workY: 0,
            workWidth: 1920,
            workHeight: 1040);

        Assert.Equal(0, offscreen.X);
        Assert.Equal(0, offscreen.Y);
        Assert.Equal(1920, offscreen.Width);
        Assert.Equal(1040, offscreen.Height);

        var secondaryMonitor = ShellWindowService.ClampBoundsToWorkArea(
            new WindowBounds(3800, 1200, 200, 150),
            workX: 1920,
            workY: 0,
            workWidth: 1280,
            workHeight: 720,
            minWidth: 520,
            minHeight: 420);

        Assert.Equal(520, secondaryMonitor.Width);
        Assert.Equal(420, secondaryMonitor.Height);
        Assert.InRange(secondaryMonitor.X, 1920, 1920 + 1280 - 520);
        Assert.InRange(secondaryMonitor.Y, 0, 720 - 420);
    }

    private sealed class DiffItem(string id, string label, int rank)
    {
        public string Id { get; } = id;
        public string Label { get; set; } = label;
        public int Rank { get; set; } = rank;
    }

    private sealed class MockOffThreadDispatcher : IUiDispatcher
    {
        private readonly AsyncLocal<bool> _onUi = new();
        public int DispatchCount { get; private set; }
        public bool HasThreadAccess => _onUi.Value;
        public bool IsOnMockUiThread => _onUi.Value;

        public bool TryEnqueue(Action action)
        {
            DispatchCount++;
            Task.Run(() =>
            {
                _onUi.Value = true;
                action();
            });
            return true;
        }

        public async Task EnqueueAsync(Action action, CancellationToken ct = default)
        {
            DispatchCount++;
            await Task.Run(() =>
            {
                _onUi.Value = true;
                action();
            }, ct);
        }

        public async Task<T> EnqueueAsync<T>(Func<T> func, CancellationToken ct = default)
        {
            DispatchCount++;
            return await Task.Run(() =>
            {
                _onUi.Value = true;
                return func();
            }, ct);
        }

        public async Task EnqueueAsync(Func<Task> asyncAction, CancellationToken ct = default)
        {
            DispatchCount++;
            await Task.Run(async () =>
            {
                _onUi.Value = true;
                await asyncAction();
            }, ct);
        }
    }
}
