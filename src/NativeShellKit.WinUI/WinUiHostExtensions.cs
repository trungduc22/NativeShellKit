using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Hosting.Hosting;
using NativeShellKit.WinUI.Dialogs;
using NativeShellKit.WinUI.Navigation;
using NativeShellKit.WinUI.Shell;
using NativeShellKit.WinUI.Theming;

namespace NativeShellKit.WinUI;

public static class WinUiHostExtensions
{
    public static NativeShellHostBuilder AddWinUiShell(this NativeShellHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.ConfigureServices(services =>
        {
            services.AddSingleton<IThemeService, WinUiThemeService>();
            services.AddSingleton<INavigationService, WinUiNavigationService>();
            services.AddSingleton<IDialogService, DialogCoordinator>();
            services.AddSingleton<IShellWindowService, ShellWindowService>();
        });
    }
}
