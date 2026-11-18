namespace UnifiedHostDemo.Modules;

public sealed record WorkspaceSnapshotRequestedEvent(string WorkspaceName);

public sealed record ArchiveIndexedEvent(string WorkspaceName, int ItemCount);
