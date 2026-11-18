using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Settings;
using NativeShellKit.Diagnostics.Logging;
using NativeShellKit.Diagnostics.Metrics;
using NativeShellKit.Security;

namespace NativeShellKit.Diagnostics;

public static class DiagnosticsServiceCollectionExtensions
{
    public static IServiceRegistry AddSecurityAndDiagnostics(this IServiceRegistry services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<DpapiSecretStore>(sp => new DpapiSecretStore(sp.GetRequiredService<AppIdentity>()));
        services.AddSingleton<IpcAuthManager>(sp => new IpcAuthManager(sp.GetRequiredService<AppIdentity>()));
        services.AddSingleton<ISdkLogger>(sp => new RedactingFileLogger(sp.GetRequiredService<AppIdentity>()));
        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
        return services;
    }
}
