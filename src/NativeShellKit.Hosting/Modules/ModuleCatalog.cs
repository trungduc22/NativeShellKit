using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Modules;

namespace NativeShellKit.Hosting.Modules;

public sealed class ModuleContext : IModuleContext
{
    public ModuleContext(AppIdentity hostIdentity, IServiceProvider services, string moduleDataDirectory)
    {
        HostIdentity = hostIdentity ?? throw new ArgumentNullException(nameof(hostIdentity));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        ModuleDataDirectory = moduleDataDirectory ?? throw new ArgumentNullException(nameof(moduleDataDirectory));
    }

    public AppIdentity HostIdentity { get; }
    public IServiceProvider Services { get; }
    public string ModuleDataDirectory { get; }
}

public sealed class ModuleCatalog : IModuleCatalog
{
    private readonly List<IShellModule> _modules = [];

    public ModuleCatalog(IEnumerable<IShellModule>? modules = null)
    {
        if (modules is not null)
        {
            foreach (var module in modules)
                AddModule(module);
        }
    }

    public IReadOnlyList<IShellModule> Modules => _modules;

    public void AddModule(IShellModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (_modules.Any(m => string.Equals(m.Descriptor.ModuleId, module.Descriptor.ModuleId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Module '{module.Descriptor.ModuleId}' is already registered.");
        _modules.Add(module);
    }

    public IShellModule? FindModule(string moduleId) =>
        _modules.FirstOrDefault(m => string.Equals(m.Descriptor.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ModuleNavigationItem> GetAllNavigationItems() =>
        _modules
            .SelectMany(m => m.GetNavigationItems())
            .OrderBy(n => n.IsFooterItem)
            .ThenBy(n => n.Order)
            .ThenBy(n => n.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<ModuleCommandDescriptor> GetAllCommands() =>
        _modules.SelectMany(m => m.GetCommands()).ToArray();

    public async Task<bool> ExecuteCommandAsync(string commandId, IServiceProvider services, CancellationToken ct = default)
    {
        var cmd = GetAllCommands()
            .FirstOrDefault(c => string.Equals(c.CommandId, commandId, StringComparison.OrdinalIgnoreCase));

        if (cmd is null)
            return false;

        await cmd.ExecuteAsync(services, ct).ConfigureAwait(false);
        return true;
    }
}
