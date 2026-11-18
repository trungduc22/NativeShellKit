using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Modules;
using NativeShellKit.Abstractions.Settings;
using NativeShellKit.Hosting.DependencyInjection;
using NativeShellKit.Hosting.Modules;
using NativeShellKit.Hosting.Settings;

namespace NativeShellKit.Hosting.Hosting;

public sealed class NativeShellHostBuilder
{
    private readonly AppIdentity _identity;
    private readonly string[] _args;
    private readonly List<Action<IServiceRegistry>> _serviceConfigurators = [];
    private readonly List<IShellModule> _modules = [];

    private NativeShellHostBuilder(AppIdentity identity, string[]? args)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _args = args ?? Array.Empty<string>();
    }

    public static NativeShellHostBuilder Create(AppIdentity identity, string[]? args = null) =>
        new(identity, args);

    public NativeShellHostBuilder ConfigureServices(Action<IServiceRegistry> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _serviceConfigurators.Add(configure);
        return this;
    }

    public NativeShellHostBuilder AddModule(IShellModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        _modules.Add(module);
        return this;
    }

    public NativeShellHostBuilder AddModule<TModule>() where TModule : class, IShellModule, new()
    {
        _modules.Add(new TModule());
        return this;
    }

    public NativeShellHost Build()
    {
        var container = new ServiceContainer();
        var startupContext = new AppStartupContext(
            _identity,
            _args,
            StartedFromAutoStart: _args.Any(a => string.Equals(a, "--autostart", StringComparison.OrdinalIgnoreCase)),
            IsSelfTestMode: _args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)));

        var lifecycleService = new AppLifecycleService(startupContext);
        var moduleCatalog = new ModuleCatalog(_modules);
        var settingsService = new SettingsService(_identity);

        container.AddSingleton(_identity);
        container.AddSingleton<IServiceRegistry>(container);
        container.AddSingleton<IAppLifecycleService>(lifecycleService);
        container.AddSingleton<IModuleCatalog>(moduleCatalog);
        container.AddSingleton<ISettingsService>(settingsService);
        container.AddSingleton<IBackgroundTaskSupervisor>(sp => new BackgroundTaskSupervisor(sp.GetService<ISdkLogger>()));

        foreach (var configure in _serviceConfigurators)
        {
            configure(container);
        }

        foreach (var module in _modules)
        {
            module.RegisterServices(container);
        }

        var supervisor = container.GetRequiredService<IBackgroundTaskSupervisor>();
        var host = new NativeShellHost(_identity, container, moduleCatalog, lifecycleService, supervisor);
        container.AddSingleton<IAppHost>(host);
        return host;
    }
}
