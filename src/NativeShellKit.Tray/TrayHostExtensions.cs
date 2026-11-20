using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Tray;
using NativeShellKit.Hosting.Hosting;
using NativeShellKit.Windows.Interop;
using NativeShellKit.Windows.SystemServices;

namespace NativeShellKit.Tray;

public static class TrayHostExtensions
{
    public static IServiceRegistry AddNativeTray(this IServiceRegistry services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<NativeTrayService>(sp => new NativeTrayService(
            sp.GetRequiredService<AppIdentity>(),
            sp.GetService<IWin32Interop>(),
            sp.GetService<WindowManagerService>()));
        services.AddSingleton<ITrayService>(sp => sp.GetRequiredService<NativeTrayService>());
        return services;
    }

    public static NativeShellHostBuilder AddNativeTray(this NativeShellHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.ConfigureServices(services => services.AddNativeTray());
    }
}
