using Microsoft.UI.Xaml;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.DesignSystem.Themes;

namespace BackgroundCompanionApp;

public sealed partial class App : Application
{
    private readonly IAppHost _host;
    private MainWindow? _window;

    public App(IAppHost host)
    {
        _host = host;
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        DesignSystemThemeResources.EnsureLoaded();
        await _host.StartAsync();
        _window = new MainWindow(_host);
        _window.Activate();
    }
}
