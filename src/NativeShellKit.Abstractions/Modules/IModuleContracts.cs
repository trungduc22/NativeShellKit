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

public interface IShellModule
{
    ModuleDescriptor Descriptor { get; }
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
}
