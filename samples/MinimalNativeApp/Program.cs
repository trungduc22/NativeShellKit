using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Settings;
using NativeShellKit.Diagnostics;
using NativeShellKit.Hosting.Hosting;
using NativeShellKit.Windows;
using NativeShellKit.WinUI;
using WinRT;

namespace MinimalNativeApp;

public sealed class MinimalAppPreferences
{
    public AppThemeMode Theme { get; set; } = AppThemeMode.System;
    public string AccentHex { get; set; } = "#0F766E";
}

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        bool isSelfTest = args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase));
        string? interactiveOutputDir = ParseInteractiveOutputDir(args);
        bool isolatedRun = isSelfTest || interactiveOutputDir is not null;
        string? tempRoot = isolatedRun ? Path.Combine(Path.GetTempPath(), $"nsk-min-test-{Guid.NewGuid():N}") : null;

        try
        {
            var identity = new AppIdentity("minimalnativeapp", "Minimal Native App", "NativeShellKit", "0.1.0", tempRoot);
            var builder = NativeShellHostBuilder.Create(identity, args)
                .AddWinUiShell()
                .ConfigureServices(services =>
                {
                    services.AddWindowsServices();
                    services.AddSecurityAndDiagnostics();
                });

            var host = builder.Build();
            try
            {
                if (isSelfTest)
                {
                    return RunSelfTestAsync(host).GetAwaiter().GetResult();
                }

                int interactiveExitCode = 0;
                ComWrappersSupport.InitializeComWrappers();
                Application.Start(callbackParams =>
                {
                    SynchronizationContext.SetSynchronizationContext(
                        new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                    _ = new App(host, interactiveOutputDir, code => interactiveExitCode = code);
                });
                host.StopAsync().GetAwaiter().GetResult();
                return interactiveOutputDir is not null ? interactiveExitCode : 0;
            }
            finally
            {
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        finally
        {
            if (tempRoot is not null && Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string? ParseInteractiveOutputDir(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--interactive-test", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    return Path.GetFullPath(args[i + 1]);
                return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "interactive-screenshots"));
            }
        }
        return null;
    }

    private static async Task<int> RunSelfTestAsync(NativeShellHost host)
    {
        await host.StartAsync();

        var nav = host.Services.GetRequiredService<INavigationService>();
        var theme = host.Services.GetRequiredService<IThemeService>();
        var settings = host.Services.GetRequiredService<ISettingsService>();

        MainWindow.RegisterRoutes(nav, host, theme, settings);

        bool nav1 = nav.Navigate("overview");
        bool nav2 = nav.Navigate("controls");
        bool nav3 = nav.Navigate("settings");
        bool wentBack = nav.GoBack();

        theme.ApplyTheme(AppThemeMode.Dark, "#0F766E");
        bool darkApplied = theme.IsDarkEffective;
        theme.ApplyTheme(AppThemeMode.Light, "#0F766E");
        bool lightApplied = !theme.IsDarkEffective;
        theme.ApplyTheme(AppThemeMode.System, "#0F766E");

        var store = settings.GetStore<MinimalAppPreferences>();
        await store.SaveAsync(new MinimalAppPreferences { Theme = AppThemeMode.Dark, AccentHex = "#0F766E" });
        var loaded = await store.LoadAsync();

        await host.StopAsync();

        bool ok = nav1 && nav2 && nav3 && wentBack && darkApplied && lightApplied &&
                  loaded.Theme == AppThemeMode.Dark &&
                  string.Equals(loaded.AccentHex, "#0F766E", StringComparison.OrdinalIgnoreCase);

        Console.WriteLine($"MINIMAL_APP_SELF_TEST_PASSED routes={nav.RegisteredRoutes.Count} themeSwitch=True settingsPersisted={loaded.Theme}");
        return ok ? 0 : 1;
    }
}
