namespace NativeShellKit.Windows.Platform;

public sealed record WindowsVersionInfo(
    Version OsVersion,
    int BuildNumber,
    bool IsWindows10OrLater,
    bool IsWindows11OrLater,
    bool SupportsMica,
    bool SupportsAcrylic,
    bool Is64BitProcess);

public sealed class WindowsVersionService
{
    public WindowsVersionInfo GetCurrent()
    {
        var ver = Environment.OSVersion.Version;
        int build = ver.Build;
        bool isWin10 = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);
        bool isWin11 = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

        return new WindowsVersionInfo(
            ver,
            build,
            isWin10,
            isWin11,
            SupportsMica: isWin11,
            SupportsAcrylic: isWin10,
            Is64BitProcess: Environment.Is64BitProcess);
    }
}
