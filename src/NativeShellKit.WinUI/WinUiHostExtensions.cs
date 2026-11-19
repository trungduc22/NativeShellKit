using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Threading;
using NativeShellKit.Hosting.Hosting;
using NativeShellKit.WinUI.Dialogs;
using NativeShellKit.WinUI.Navigation;
using NativeShellKit.WinUI.Shell;
using NativeShellKit.WinUI.Theming;
using NativeShellKit.WinUI.Threading;

namespace NativeShellKit.WinUI;

public static class WinUiHostExtensions
{
    public static NativeShellHostBuilder AddWinUiShell(this NativeShellHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.ConfigureServices(services =>
        {
            services.AddSingleton<WinUiDispatcherService, WinUiDispatcherService>();
            services.AddSingleton<IUiDispatcher>(sp => sp.GetRequiredService<WinUiDispatcherService>());
            services.AddSingleton<IThemeService, WinUiThemeService>();
            services.AddSingleton<INavigationService, WinUiNavigationService>();
            services.AddSingleton<IDialogService, DialogCoordinator>();
            services.AddSingleton<ShellWindowService>(sp => new ShellWindowService(sp.GetService<IUiDispatcher>()));
            services.AddSingleton<IShellWindowService>(sp => sp.GetRequiredService<ShellWindowService>());
        });
    }
}
