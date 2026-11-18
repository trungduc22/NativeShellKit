using NativeShellKit.Abstractions.Navigation;

namespace NativeShellKit.WinUI.Navigation;

public sealed class WinUiNavigationService : INavigationService
{
    private readonly List<NavigationRouteDescriptor> _routes = [];
    private readonly Stack<(string Route, object? Parameter)> _backStack = new();
    private object? _currentParameter;

    public string? CurrentRoute { get; private set; }
    public bool CanGoBack => _backStack.Count > 0;
    public IReadOnlyList<NavigationRouteDescriptor> RegisteredRoutes =>
        _routes.OrderBy(r => r.IsFooterItem).ThenBy(r => r.Order).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToArray();

    public event EventHandler<NavigationEventArgs>? Navigated;

    public void RegisterRoute(NavigationRouteDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _routes.RemoveAll(r => string.Equals(r.Route, descriptor.Route, StringComparison.OrdinalIgnoreCase));
        _routes.Add(descriptor);
    }

    public NavigationRouteDescriptor? FindRoute(string route) =>
        _routes.FirstOrDefault(r => string.Equals(r.Route, route, StringComparison.OrdinalIgnoreCase));

    public bool Navigate(string route, object? parameter = null)
    {
        if (string.IsNullOrWhiteSpace(route))
            return false;

        var descriptor = FindRoute(route);
        if (descriptor is null)
            return false;

        var previous = CurrentRoute;
        if (!string.IsNullOrWhiteSpace(previous) && !string.Equals(previous, descriptor.Route, StringComparison.OrdinalIgnoreCase))
        {
            _backStack.Push((previous, _currentParameter));
        }

        CurrentRoute = descriptor.Route;
        _currentParameter = parameter;
        Navigated?.Invoke(this, new NavigationEventArgs(descriptor.Route, parameter, previous));
        return true;
    }

    public bool GoBack()
    {
        if (_backStack.Count == 0)
            return false;

        var previous = CurrentRoute;
        var (targetRoute, param) = _backStack.Pop();
        CurrentRoute = targetRoute;
        _currentParameter = param;
        Navigated?.Invoke(this, new NavigationEventArgs(targetRoute, param, previous));
        return true;
    }
}
