namespace NativeShellKit.Abstractions.Tray;

public sealed record TrayMenuDescriptor(
    uint Id,
    string Text,
    Action? Callback = null,
    bool IsSeparator = false,
    bool IsEnabled = true);

public interface ITrayService : IDisposable
{
    bool IsIconActive { get; }
    bool HasTaskbarShell { get; }
    bool MinimizeToTrayOnClose { get; set; }
    string? LastTrayError { get; }

    event EventHandler? RestoreRequested;
    event EventHandler? FullExitRequested;

    bool Initialize(nint ownerHwnd, string? iconPath = null);
    void SetMenuItems(IEnumerable<TrayMenuDescriptor> items);
    void UpdateTooltip(string tooltip);
    bool HandleWindowClosing();
    void HideToTray();
    void ShowFromTray();
    void RequestFullExit();
    bool RecoverAfterExplorerRestart();
}
