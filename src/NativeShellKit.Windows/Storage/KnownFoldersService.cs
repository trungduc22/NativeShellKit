using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Windows.Interop;

namespace NativeShellKit.Windows.Storage;

public sealed class KnownFoldersService
{
    private static readonly Guid DownloadsFolderGuid = new("374DE290-123F-4565-9164-39C4925E467B");
    private readonly IWin32Interop _interop;

    public KnownFoldersService(IWin32Interop? interop = null)
    {
        _interop = interop ?? Win32Interop.Default;
    }

    public string Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public string RoamingAppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public string StartupFolder => Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    public string Downloads =>
        _interop.GetKnownFolderPath(DownloadsFolderGuid)
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public string GetIsolatedAppDirectory(AppIdentity identity, string? subFolder = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        identity.EnsureDirectoriesExist();

        if (string.IsNullOrWhiteSpace(subFolder))
            return identity.DataDirectory;

        var cleanSub = Path.GetFileName(subFolder.Trim());
        var target = Path.Combine(identity.DataDirectory, cleanSub);
        Directory.CreateDirectory(target);
        return target;
    }
}
