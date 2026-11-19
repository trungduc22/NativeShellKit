namespace NativeShellKit.Abstractions.Navigation;

public enum NavigationCachePolicy
{
    Cached,
    Transient
}

public interface INavigationAware
{
    void OnNavigatedTo(object? parameter);
    void OnNavigatedFrom();
}

public sealed record NavigationRouteDescriptor(
    string Route,
    string Title,
    string Glyph,
    Func<object> ViewFactory,
    string? ModuleId = null,
    int Order = 0,
    bool IsFooterItem = false,
    NavigationCachePolicy CachePolicy = NavigationCachePolicy.Cached);

public sealed class NavigationEventArgs : EventArgs
{
    public NavigationEventArgs(string route, object? parameter, string? previousRoute)
    {
        Route = route;
        Parameter = parameter;
        PreviousRoute = previousRoute;
    }

    public string Route { get; }
    public object? Parameter { get; }
    public string? PreviousRoute { get; }
}

public interface INavigationService
{
    string? CurrentRoute { get; }
    bool CanGoBack { get; }
    IReadOnlyList<NavigationRouteDescriptor> RegisteredRoutes { get; }
    event EventHandler<NavigationEventArgs>? Navigated;

    void RegisterRoute(NavigationRouteDescriptor descriptor);
    bool Navigate(string route, object? parameter = null);
    bool GoBack();
}

public enum AppThemeMode
{
    System,
    Light,
    Dark
}

public sealed record AccentColorPalette(
    string PrimaryHex,
    string HoverHex,
    string PressedHex,
    string SubtleTintHex,
    string ContrastForegroundHex);

public interface IThemeService
{
    AppThemeMode CurrentMode { get; }
    bool IsDarkEffective { get; }
    AccentColorPalette CurrentAccent { get; }
    bool IsMicaSupported { get; }

    event EventHandler? ThemeChanged;

    void ApplyTheme(AppThemeMode mode, string? customAccentHex = null);
    AccentColorPalette ComputePalette(string accentHex, bool isDark);
}

public sealed record DialogRequest(
    string Title,
    string Message,
    string PrimaryButtonText = "OK",
    string? SecondaryButtonText = null,
    string? CloseButtonText = null);

public enum DialogOutcome
{
    Primary,
    Secondary,
    Closed
}

public interface IDialogService
{
    Task<DialogOutcome> ShowMessageAsync(DialogRequest request, CancellationToken ct = default);
}

public interface IShellWindowService
{
    nint WindowHandle { get; }
    bool IsVisible { get; }
    void ShowAndActivate();
    void Hide();
    void Minimize();
    void SetTitle(string title);
}
