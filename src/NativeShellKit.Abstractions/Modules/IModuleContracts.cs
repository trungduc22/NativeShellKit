using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Identity;

namespace NativeShellKit.Abstractions.Modules;

public sealed record ModuleDescriptor(
    string ModuleId,
    string DisplayName,
    string Version,
    string Description = "");

public sealed record ModuleNavigationItem(
    string Route,
    string Title,
    string Glyph,
    string ModuleId,
    int Order = 0,
    bool IsFooterItem = false);

public sealed record ModuleCommandDescriptor(
    string CommandId,
    string Title,
    string ModuleId,
    Func<IServiceProvider, CancellationToken, Task> ExecuteAsync);

public interface IModuleContext
{
    AppIdentity HostIdentity { get; }
    IServiceProvider Services { get; }
    string ModuleDataDirectory { get; }
}

public enum ModuleHealthState
{
    Uninitialized,
    Starting,
    Healthy,
    Degraded,
    Disabled,
    Faulted,
    Stopped
}

public sealed record ModuleHealthSnapshot(
    string ModuleId,
    ModuleHealthState State,
    string? ErrorMessage = null,
    DateTimeOffset? UpdatedUtc = null);

public interface IShellModule
{
    ModuleDescriptor Descriptor { get; }
    int StartupOrder => 100;
    bool IsCritical => false;
    IReadOnlyList<string> DependsOnModuleIds => Array.Empty<string>();
    void RegisterServices(IServiceRegistry services);
    IReadOnlyList<ModuleNavigationItem> GetNavigationItems();
    IReadOnlyList<ModuleCommandDescriptor> GetCommands();
    Task InitializeAsync(IModuleContext context, CancellationToken ct = default);
    Task ShutdownAsync(CancellationToken ct = default);
}

public interface IModuleCatalog
{
    IReadOnlyList<IShellModule> Modules { get; }
    IReadOnlyList<ModuleNavigationItem> GetAllNavigationItems();
    IReadOnlyList<ModuleCommandDescriptor> GetAllCommands();
    IShellModule? FindModule(string moduleId);
    Task<bool> ExecuteCommandAsync(string commandId, IServiceProvider services, CancellationToken ct = default);
    IReadOnlyList<ModuleHealthSnapshot> GetHealthSnapshots() => Array.Empty<ModuleHealthSnapshot>();
    ModuleHealthSnapshot? GetHealthSnapshot(string moduleId) => null;
}
