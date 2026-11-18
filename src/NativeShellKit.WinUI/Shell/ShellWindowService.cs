using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using NativeShellKit.Abstractions.Navigation;
using WinRT.Interop;

namespace NativeShellKit.WinUI.Shell;

public sealed class ShellWindowService : IShellWindowService
{
    private Window? _window;
    private AppWindow? _appWindow;

    public nint WindowHandle { get; private set; }
    public bool IsVisible { get; private set; }

    public void AttachWindow(Window window, UIElement? titleBarElement = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
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
}
