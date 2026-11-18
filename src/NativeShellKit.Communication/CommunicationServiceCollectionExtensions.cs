using NativeShellKit.Abstractions.Communication;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Communication.InProcess;
using NativeShellKit.Communication.Ipc;
using NativeShellKit.Security;

namespace NativeShellKit.Communication;

public static class CommunicationServiceCollectionExtensions
{
    public static IServiceRegistry AddCommunicationServices(this IServiceRegistry services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IInProcessEventBus, InProcessEventBus>();
        services.AddSingleton<IpcAuthManager>(sp => new IpcAuthManager(sp.GetRequiredService<AppIdentity>()));
        services.AddSingleton<IIpcServer>(sp => new NamedPipeIpcServer(
            sp.GetRequiredService<AppIdentity>(),
            sp.GetRequiredService<IpcAuthManager>()));
        return services;
    }
}
