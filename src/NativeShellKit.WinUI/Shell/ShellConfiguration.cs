using NativeShellKit.Abstractions.Navigation;

namespace NativeShellKit.WinUI.Shell;

public enum ShellMode
{
    Dashboard,
    ModuleHost,
    CompactUtility,
    SinglePage
}

public enum ShellDensity
{
    Standard,
    Compact
}

public enum ShellTrayPolicy
{
    WindowFirst,
    TrayFirst
}

public sealed record ShellConfiguration(
    ShellMode Mode = ShellMode.Dashboard,
    ShellDensity Density = ShellDensity.Standard,
    ShellTrayPolicy TrayPolicy = ShellTrayPolicy.WindowFirst,
    ShellBackdropKind Backdrop = ShellBackdropKind.MicaBase,
    AccentSourceMode AccentMode = AccentSourceMode.System,
    string? BrandAccentHex = null,
    double NavWideThreshold = 1008,
    double NavCompactThreshold = 641);
