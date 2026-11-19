using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, ModuleHealthSnapshot> _health = new(StringComparer.OrdinalIgnoreCase);

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
        _health[module.Descriptor.ModuleId] = new ModuleHealthSnapshot(
            module.Descriptor.ModuleId,
            ModuleHealthState.Uninitialized,
            UpdatedUtc: DateTimeOffset.UtcNow);
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

    public IReadOnlyList<ModuleHealthSnapshot> GetHealthSnapshots() =>
        _modules
            .Select(m => GetHealthSnapshot(m.Descriptor.ModuleId)!)
            .ToArray();

    public ModuleHealthSnapshot? GetHealthSnapshot(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
            return null;
        return _health.TryGetValue(moduleId, out var snapshot) ? snapshot : null;
    }

    internal void SetHealth(string moduleId, ModuleHealthState state, string? errorMessage = null)
    {
        _health[moduleId] = new ModuleHealthSnapshot(moduleId, state, errorMessage, DateTimeOffset.UtcNow);
    }

    internal IReadOnlyList<IShellModule> GetStartupOrderedModules()
    {
        if (_modules.Count <= 1)
        {
            if (_modules.Count == 1)
            {
                var single = _modules[0];
                foreach (var dep in single.DependsOnModuleIds)
                {
                    if (!string.IsNullOrWhiteSpace(dep))
                        throw new InvalidOperationException($"Module '{single.Descriptor.ModuleId}' depends on missing module '{dep}'.");
                }
            }
            return _modules.ToArray();
        }

        var indexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _modules.Count; i++)
            indexById[_modules[i].Descriptor.ModuleId] = i;

        var inDegree = new int[_modules.Count];
        var dependents = new List<int>[_modules.Count];
        for (int i = 0; i < _modules.Count; i++)
            dependents[i] = [];

        for (int i = 0; i < _modules.Count; i++)
        {
            var mod = _modules[i];
            var seenDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawDep in mod.DependsOnModuleIds)
            {
                if (string.IsNullOrWhiteSpace(rawDep))
                    continue;
                var dep = rawDep.Trim();
                if (!seenDeps.Add(dep))
                    continue;

                if (!indexById.TryGetValue(dep, out int depIdx))
                    throw new InvalidOperationException($"Module '{mod.Descriptor.ModuleId}' depends on missing module '{dep}'.");

                if (depIdx == i)
                    throw new InvalidOperationException($"Module '{mod.Descriptor.ModuleId}' cannot depend on itself.");

                dependents[depIdx].Add(i);
                inDegree[i]++;
            }
        }

        var ready = new List<int>();
        for (int i = 0; i < _modules.Count; i++)
        {
            if (inDegree[i] == 0)
                ready.Add(i);
        }

        var ordered = new List<IShellModule>(_modules.Count);
        while (ready.Count > 0)
        {
            ready.Sort((a, b) =>
            {
                int cmp = _modules[a].StartupOrder.CompareTo(_modules[b].StartupOrder);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });

            int next = ready[0];
            ready.RemoveAt(0);
            ordered.Add(_modules[next]);

            foreach (int child in dependents[next])
            {
                inDegree[child]--;
                if (inDegree[child] == 0)
                    ready.Add(child);
            }
        }

        if (ordered.Count != _modules.Count)
        {
            var stuck = _modules
                .Where((_, idx) => inDegree[idx] > 0)
                .Select(m => m.Descriptor.ModuleId);
            throw new InvalidOperationException($"Circular module dependency detected involving: {string.Join(", ", stuck)}.");
        }

        return ordered;
    }
}
