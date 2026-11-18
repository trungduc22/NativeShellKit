namespace NativeShellKit.Tests;

internal sealed class TempRootScope : IDisposable
{
    public TempRootScope(string prefix = "nsk-test")
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NativeShellKit.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate NativeShellKit.sln ancestor directory.");
    }

    public void Dispose()
    {
        if (!Directory.Exists(RootPath))
            return;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
                return;
            }
            catch
            {
                Thread.Sleep(30);
            }
        }
    }
}
