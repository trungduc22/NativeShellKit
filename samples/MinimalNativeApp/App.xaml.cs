using Microsoft.UI.Xaml;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.DesignSystem.Themes;

namespace MinimalNativeApp;

public sealed partial class App : Application
{
    private readonly IAppHost _host;
    private readonly string? _interactiveOutputDir;
    private readonly Action<int>? _onAcceptanceComplete;
    private MainWindow? _window;

    public App(
        IAppHost host,
        string? interactiveOutputDir = null,
        Action<int>? onAcceptanceComplete = null)
    {
        _host = host;
        _interactiveOutputDir = interactiveOutputDir;
        _onAcceptanceComplete = onAcceptanceComplete;
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine($"MinimalNativeApp UnhandledException: {e.Message}\n{e.Exception}");
            _onAcceptanceComplete?.Invoke(3);
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            DesignSystemThemeResources.EnsureLoaded();
            await _host.StartAsync();
            _window = new MainWindow(_host, _interactiveOutputDir, _onAcceptanceComplete);
            _window.Activate();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MinimalNativeApp OnLaunched error: {ex}");
            _onAcceptanceComplete?.Invoke(4);
            Exit();
        }
    }
}
