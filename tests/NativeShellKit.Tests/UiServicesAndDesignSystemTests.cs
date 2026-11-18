using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.DesignSystem.Tokens;
using NativeShellKit.WinUI.Navigation;
using NativeShellKit.WinUI.Theming;
using Xunit;

namespace NativeShellKit.Tests;

public sealed class UiServicesAndDesignSystemTests
{
    [Fact]
    public void ThemeService_SwitchesModes_ComputesContrastPalettes_AndFiresEvents()
    {
        var theme = new WinUiThemeService();
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

        var lightPalette = theme.ComputePalette("#FFFFFF", isDark: false);
        Assert.Equal("#0F172A", lightPalette.ContrastForegroundHex);

        var darkAccentOnLight = theme.ComputePalette("#1E3A8A", isDark: false);
        Assert.Equal("#FFFFFF", darkAccentOnLight.ContrastForegroundHex);

        var darkPalette = theme.ComputePalette("#1E3A8A", isDark: true);
        Assert.StartsWith("#", darkPalette.PrimaryHex);
        Assert.StartsWith("#", darkPalette.HoverHex);
        Assert.StartsWith("#", darkPalette.PressedHex);
        Assert.StartsWith("#", darkPalette.SubtleTintHex);
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
        nav.RegisterRoute(new NavigationRouteDescriptor("details", "Details", "\uE946", () => "details", Order: 2));
        nav.RegisterRoute(new NavigationRouteDescriptor("home", "Home", "\uE80F", () => "home", Order: 0));

        Assert.Equal(["home", "details", "settings"], nav.RegisteredRoutes.Select(r => r.Route).ToArray());

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
}
