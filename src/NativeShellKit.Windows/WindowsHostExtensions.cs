using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Windows.Interop;
using NativeShellKit.Windows.Platform;
using NativeShellKit.Windows.SingleInstance;
using NativeShellKit.Windows.Startup;
using NativeShellKit.Windows.Storage;
using NativeShellKit.Windows.SystemServices;

namespace NativeShellKit.Windows;

public static class WindowsServiceCollectionExtensions
{
    public static IServiceRegistry AddWindowsServices(
        this IServiceRegistry services,
        IStartupRegistryBackend? startupBackend = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IWin32Interop>(Win32Interop.Default);
        services.AddSingleton<WindowsVersionService, WindowsVersionService>();
        services.AddSingleton<KnownFoldersService>(sp => new KnownFoldersService(sp.GetRequiredService<IWin32Interop>()));
        services.AddSingleton<WindowManagerService>(sp => new WindowManagerService(sp.GetRequiredService<IWin32Interop>()));
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<FilePickerService, FilePickerService>();
        services.AddSingleton<ProcessLaunchService, ProcessLaunchService>();
        services.AddSingleton<NetworkAndPowerMonitor, NetworkAndPowerMonitor>();
        services.AddSingleton<SingleInstanceCoordinator>(sp => new SingleInstanceCoordinator(sp.GetRequiredService<AppIdentity>()));
        services.AddSingleton<WindowsStartupService>(sp => new WindowsStartupService(sp.GetRequiredService<AppIdentity>(), startupBackend));
        return services;
    }
}
