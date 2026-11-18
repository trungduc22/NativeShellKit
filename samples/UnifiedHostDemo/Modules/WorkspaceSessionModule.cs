using NativeShellKit.Abstractions.Communication;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Modules;

namespace UnifiedHostDemo.Modules;

public sealed class WorkspaceSessionState
{
    public int CompletedSnapshots { get; set; }
    public string? LastCompletedWorkspace { get; set; }
}

public sealed class WorkspaceSessionModule : IShellModule
{
    private IDisposable? _subscription;
    private IInProcessEventBus? _bus;

    public bool IsInitialized { get; private set; }
    public bool IsShutdown { get; private set; }
    public WorkspaceSessionState State { get; } = new();

    public ModuleDescriptor Descriptor { get; } = new(
        ModuleId: "workspace-session",
        DisplayName: "Workspace Session",
        Version: "1.0.0",
        Description: "Manages active workspace sessions and broadcasts snapshot requests via IInProcessEventBus.");

    public void RegisterServices(IServiceRegistry services)
    {
        services.AddSingleton(State);
    }

    public IReadOnlyList<ModuleNavigationItem> GetNavigationItems() =>
    [
        new ModuleNavigationItem("sessions", "Sessions", "\uE7F4", Descriptor.ModuleId, Order: 0)
    ];

    public IReadOnlyList<ModuleCommandDescriptor> GetCommands() =>
    [
        new ModuleCommandDescriptor("sessions.snapshot", "Request Workspace Snapshot", Descriptor.ModuleId, async (_, ct) =>
        {
            if (_bus is not null)
            {
                await _bus.PublishAsync(new WorkspaceSnapshotRequestedEvent("NativeShellKit-Main"), ct);
            }
        })
    ];

    public Task InitializeAsync(IModuleContext context, CancellationToken ct = default)
    {
        _bus = context.Services.GetRequiredService<IInProcessEventBus>();
        _subscription = _bus.Subscribe<ArchiveIndexedEvent>((evt, _) =>
        {
            State.CompletedSnapshots++;
            State.LastCompletedWorkspace = evt.WorkspaceName;
            return Task.CompletedTask;
        });
        Directory.CreateDirectory(context.ModuleDataDirectory);
        IsInitialized = true;
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken ct = default)
    {
        _subscription?.Dispose();
        _subscription = null;
        IsShutdown = true;
        return Task.CompletedTask;
    }
}
