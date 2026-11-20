using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Threading;
using NativeShellKit.WinUI.Threading;
using Windows.Graphics;
using WinRT.Interop;

namespace NativeShellKit.WinUI.Shell;

public sealed record WindowBounds(int X, int Y, int Width, int Height);

public sealed class ShellWindowService : IShellWindowService
{
    private readonly WinUiDispatcherService? _dispatcher;
    private Window? _window;
    private AppWindow? _appWindow;

    public ShellWindowService(IUiDispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher as WinUiDispatcherService;
    }

    public nint WindowHandle { get; private set; }
    public bool IsVisible { get; private set; }

    public void AttachWindow(Window window, UIElement? titleBarElement = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _dispatcher?.Attach(window.DispatcherQueue);
        WindowHandle = WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WindowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (titleBarElement is not null)
        {
            window.ExtendsContentIntoTitleBar = true;
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                _appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
                _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
                _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            }
            window.SetTitleBar(titleBarElement);

            if (titleBarElement is ShellTitleBar shellTitleBar)
                shellTitleBar.AttachWindow(window);
        }

        IsVisible = true;
    }

    public void ShowAndActivate()
    {
        if (_appWindow is not null)
            _appWindow.Show();
        _window?.Activate();
        IsVisible = true;
    }

    public void Hide()
    {
        if (_appWindow is not null)
            _appWindow.Hide();
        IsVisible = false;
    }

    public void Minimize()
    {
        if (_appWindow?.Presenter is OverlappedPresenter presenter)
            presenter.Minimize();
    }

    public void SetTitle(string title)
    {
        if (_window is not null)
            _window.Title = title ?? string.Empty;
    }

    public WindowBounds? GetCurrentBounds()
    {
        if (_appWindow is null)
            return null;

        var pos = _appWindow.Position;
        var size = _appWindow.Size;
        return new WindowBounds(pos.X, pos.Y, size.Width, size.Height);
    }

    public bool RestoreBounds(WindowBounds bounds, int minWidth = 520, int minHeight = 420)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (_appWindow is null)
            return false;

        try
        {
            var probePoint = new PointInt32(bounds.X + Math.Max(bounds.Width, minWidth) / 2, bounds.Y + 24);
            var area = DisplayArea.GetFromPoint(probePoint, DisplayAreaFallback.Nearest) ?? DisplayArea.Primary;
            var workArea = area?.WorkArea ?? new RectInt32(0, 0, 1920, 1080);

            var clamped = ClampBoundsToWorkArea(
                bounds,
                workArea.X,
                workArea.Y,
                workArea.Width,
                workArea.Height,
                minWidth,
                minHeight);

            _appWindow.MoveAndResize(new RectInt32(clamped.X, clamped.Y, clamped.Width, clamped.Height));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static WindowBounds ClampBoundsToWorkArea(
        WindowBounds requested,
        int workX,
        int workY,
        int workWidth,
        int workHeight,
        int minWidth = 520,
        int minHeight = 420)
    {
        ArgumentNullException.ThrowIfNull(requested);
        int safeWorkWidth = Math.Max(workWidth, minWidth);
        int safeWorkHeight = Math.Max(workHeight, minHeight);

        int width = Math.Clamp(requested.Width, Math.Min(minWidth, safeWorkWidth), safeWorkWidth);
        int height = Math.Clamp(requested.Height, Math.Min(minHeight, safeWorkHeight), safeWorkHeight);

        int maxX = workX + safeWorkWidth - width;
        int maxY = workY + safeWorkHeight - height;
        int x = Math.Clamp(requested.X, workX, Math.Max(workX, maxX));
        int y = Math.Clamp(requested.Y, workY, Math.Max(workY, maxY));

        return new WindowBounds(x, y, width, height);
    }
}
