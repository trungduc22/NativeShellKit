using System.Diagnostics;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Settings;
using NativeShellKit.Build;
using NativeShellKit.Diagnostics.Logging;
using NativeShellKit.Diagnostics.Metrics;
using NativeShellKit.Diagnostics.Verification;
using Xunit;

namespace NativeShellKit.Tests;

public sealed class DiagnosticsAndPackagingTests
{
    [Fact]
    public async Task DiagnosticsAndSelfTestRunner_RedactLogsCaptureMetricsAndWriteJsonReport()
    {
        using var tmp = new TempRootScope();
        var identity = new AppIdentity("diagapp", "Diag App", baseDataRoot: tmp.RootPath);

        var logger = new RedactingFileLogger(identity);
        logger.Log(SdkLogLevel.Info, "Auth", "Token=Bearer supersecrettoken123 api_key=my_key_99");
        var recent = logger.GetRecentLines(5);
        Assert.Single(recent);
        Assert.DoesNotContain("supersecrettoken123", recent[0]);
        Assert.DoesNotContain("my_key_99", recent[0]);
        Assert.True(File.Exists(logger.CurrentLogFilePath));

        var diag = new DiagnosticsService();
        diag.RecordStartupPhase("Bootstrap", 12.5);
        var timings = diag.GetStartupTimings();
        var metrics = diag.CaptureMetrics();
        Assert.Single(timings.Phases);
        Assert.True(metrics.WorkingSetBytes > 0);
        Assert.True(metrics.ThreadCount >= 1);

        var reportPath = Path.Combine(tmp.RootPath, "selftest.json");
        var runner = new UiSelfTestRunner()
            .AddSyncCheck("IdentityCheck", () => (true, identity.AppUserModelId))
            .AddSyncCheck("LoggerCheck", () => (recent.Count == 1, "Redacted"));

        var report = await runner.RunAsync(reportPath);
        Assert.True(report.AllPassed);
        Assert.True(File.Exists(reportPath));
    }

    [Fact]
    public void Packaging_AllPackagesValidAndZeroForbiddenArtifactsInSourceTree()
    {
        var repoRoot = TempRootScope.FindRepoRoot();

        foreach (var folder in new[] { "src", "samples", "templates", "tools" })
        {
            var leaks = ReleasePackagingValidator.FindForbiddenArtifacts(Path.Combine(repoRoot, folder));
            Assert.Empty(leaks);
        }

        var packagesDir = Path.Combine(repoRoot, "artifacts", "packages");
        Assert.True(Directory.Exists(packagesDir), $"Missing packages dir at {packagesDir}");
        var packages = Directory.GetFiles(packagesDir, "NativeShellKit.*.nupkg");
        Assert.True(packages.Length >= 13, $"Expected >=13 packages, found {packages.Length}");

        foreach (var pkg in packages)
        {
            var name = Path.GetFileName(pkg);
            bool isWinUi = name.StartsWith("NativeShellKit.WinUI.", StringComparison.OrdinalIgnoreCase) ||
                           name.StartsWith("NativeShellKit.DesignSystem.", StringComparison.OrdinalIgnoreCase);
            bool isTemplate = name.StartsWith("NativeShellKit.Templates.", StringComparison.OrdinalIgnoreCase);
            var res = ReleasePackagingValidator.ValidatePackageArchive(pkg, isWinUi, isTemplate);
            Assert.True(res.IsValid, $"{name}: {res.ErrorMessage}");

            if (name.StartsWith("NativeShellKit.Abstractions.", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("NativeShellKit.Hosting.", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("NativeShellKit.Build.", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(pkg);
                Assert.Contains(archive.Entries, e => e.FullName.Replace('\\', '/').StartsWith("lib/net10.0/", StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void DeveloperKitInstaller_InstallsUpgradesVerifiesAndUninstallsWithoutTouchingUserData()
    {
        var repoRoot = TempRootScope.FindRepoRoot();
        var setupExe = Path.Combine(repoRoot, "artifacts", "developer-kit", "NativeShellKit-DeveloperKit-Setup-x64.exe");
        var payloadDir = Path.Combine(repoRoot, "artifacts", "developer-kit", "payload");
        Assert.True(File.Exists(setupExe), $"Setup exe not found: {setupExe}");
        Assert.True(Directory.Exists(payloadDir), $"Payload dir not found: {payloadDir}");

        using var tmp = new TempRootScope("nsk-installer-xunit");
        var installDir = Path.Combine(tmp.RootPath, "DeveloperKit");
        var simulatedUserData = Path.Combine(tmp.RootPath, "UserAppData", "secrets.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(simulatedUserData)!);
        File.WriteAllText(simulatedUserData, "keep-me");

        RunSetup(setupExe, $"--install --components packages,cli --install-dir \"{installDir}\" --payload-dir \"{payloadDir}\" --skip-dotnet-registration");
        Assert.True(Directory.Exists(Path.Combine(installDir, "packages")));
        Assert.True(Directory.Exists(Path.Combine(installDir, "tools", "nsk")));
        Assert.False(Directory.Exists(Path.Combine(installDir, "samples")));
        RunSetup(setupExe, $"--verify --install-dir \"{installDir}\"");
        RunSetup(setupExe, $"--upgrade --install-dir \"{installDir}\" --payload-dir \"{payloadDir}\" --skip-dotnet-registration");
        Assert.True(Directory.Exists(Path.Combine(installDir, "samples")));
        RunSetup(setupExe, $"--verify --install-dir \"{installDir}\"");
        RunSetup(setupExe, $"--uninstall --install-dir \"{installDir}\" --skip-dotnet-registration");

        Assert.False(Directory.Exists(installDir));
        Assert.True(File.Exists(simulatedUserData));
    }

    private static void RunSetup(string exe, string args)
    {
        using var proc = Process.Start(new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Failed to start setup process.");

        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(30000);
        Assert.True(proc.ExitCode == 0, $"Setup exited {proc.ExitCode}: {stdout}\n{stderr}");
    }
}
