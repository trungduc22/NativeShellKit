using NativeShellKit.Abstractions.Communication;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Modules;
using NativeShellKit.Abstractions.Notifications;

namespace UnifiedHostDemo.Modules;

public sealed class ArchiveState
{
    public int LastIndexedCount { get; set; }
    public string? LastWorkspace { get; set; }
}

public sealed class CodeArchiveCompanionModule : IShellModule
{
    private IDisposable? _subscription;
    private IInProcessEventBus? _bus;
    private INotificationService? _notifications;

    public bool IsInitialized { get; private set; }
    public bool IsShutdown { get; private set; }
    public ArchiveState State { get; } = new();

    public ModuleDescriptor Descriptor { get; } = new(
        ModuleId: "code-archive-companion",
        DisplayName: "Code Archive Companion",
        Version: "1.0.0",
        Description: "Indexes workspace snapshots and notifies users without coupling to Workspace Session.");

    public void RegisterServices(IServiceRegistry services)
    {
        services.AddSingleton(State);
    }

    public IReadOnlyList<ModuleNavigationItem> GetNavigationItems() =>
    [
        new ModuleNavigationItem("archive", "Code Archive", "\uE8B7", Descriptor.ModuleId, Order: 1)
    ];

    public IReadOnlyList<ModuleCommandDescriptor> GetCommands() =>
    [
        new ModuleCommandDescriptor("archive.reindex", "Reindex Default Archive", Descriptor.ModuleId, async (_, ct) =>
        {
            await HandleSnapshotRequestAsync(new WorkspaceSnapshotRequestedEvent("default-workspace"), ct);
        })
    ];

    public Task InitializeAsync(IModuleContext context, CancellationToken ct = default)
    {
        _bus = context.Services.GetRequiredService<IInProcessEventBus>();
        _notifications = context.Services.GetService<INotificationService>();
        _subscription = _bus.Subscribe<WorkspaceSnapshotRequestedEvent>(HandleSnapshotRequestAsync);
        Directory.CreateDirectory(context.ModuleDataDirectory);
        IsInitialized = true;
        return Task.CompletedTask;
    }

    private async Task HandleSnapshotRequestAsync(WorkspaceSnapshotRequestedEvent evt, CancellationToken ct)
    {
        State.LastWorkspace = evt.WorkspaceName;
        State.LastIndexedCount += 42;

        if (_bus is not null)
        {
            await _bus.PublishAsync(new ArchiveIndexedEvent(evt.WorkspaceName, State.LastIndexedCount), ct);
        }

        if (_notifications is not null)
        {
            await _notifications.PublishAsync(new AppNotificationRequest(
                "Archive Indexed",
                $"Workspace '{evt.WorkspaceName}' snapshot indexed ({State.LastIndexedCount} symbols).",
                NotificationSeverity.Success,
                Category: "Archive",
                SourceModuleId: Descriptor.ModuleId,
                DeduplicationKey: $"archive-{evt.WorkspaceName}-{State.LastIndexedCount}"), ct);
        }
    }

    public Task ShutdownAsync(CancellationToken ct = default)
    {
        _subscription?.Dispose();
        _subscription = null;
        IsShutdown = true;
        return Task.CompletedTask;
    }
}
