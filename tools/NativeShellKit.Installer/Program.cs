using System.Windows.Forms;

namespace NativeShellKit.Installer;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(a => a is "--help" or "-h" or "/?"))
        {
            PrintUsage();
            return 0;
        }

        string installDir = Path.GetFullPath(GetArgValue(args, "--install-dir") ?? InstallerEngine.DefaultInstallDir);
        string payloadDir = Path.GetFullPath(GetArgValue(args, "--payload-dir") ?? InstallerEngine.ResolveDefaultPayloadDir());
        bool skipRegistration = args.Any(a => string.Equals(a, "--skip-dotnet-registration", StringComparison.OrdinalIgnoreCase));
        var components = InstallerEngine.ParseComponents(GetArgValue(args, "--components"), skipRegistration);

        if (args.Any(a => a.Equals("--gui-self-test", StringComparison.OrdinalIgnoreCase)))
        {
            string screenshotsDir = Path.GetFullPath(
                GetArgValue(args, "--gui-self-test") ?? Path.Combine(AppContext.BaseDirectory, "gui-self-test-screenshots"));

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using var form = new DeveloperKitInstallerWizardForm(installDir, payloadDir, skipDotNetRegistration: true, screenshotsDir);
            Application.Run(form);
            if (form.SelfTestExitCode == 0)
                Console.WriteLine($"GUI_INSTALLER_SELF_TEST_PASSED screenshots=\"{screenshotsDir}\"");
            return form.SelfTestExitCode;
        }

        bool explicitCliAction = args.Any(a =>
            a.Equals("--install", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--upgrade", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--verify", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));

        bool launchGui = args.Any(a => a.Equals("--gui", StringComparison.OrdinalIgnoreCase)) ||
                         (args.Length == 0 && Environment.UserInteractive);

        if (launchGui)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using var form = new DeveloperKitInstallerWizardForm(installDir, payloadDir, skipRegistration);
            Application.Run(form);
            return form.DialogResult == DialogResult.OK ? 0 : 0;
        }

        string mode = ParseMode(args);
        var result = mode switch
        {
            "install" => InstallerEngine.InstallOrUpgrade(payloadDir, installDir, isUpgrade: false, components),
            "upgrade" => InstallerEngine.InstallOrUpgrade(payloadDir, installDir, isUpgrade: true, components),
            "verify" => InstallerEngine.Verify(installDir),
            "uninstall" => InstallerEngine.Uninstall(installDir, skipRegistration),
            _ => new InstallerOperationResult(false, 1, $"Unknown mode: {mode}")
        };

        if (result.Success)
            Console.WriteLine(result.Message);
        else
            Console.Error.WriteLine($"[fail] {result.Message}");

        return result.ExitCode;
    }

    private static string ParseMode(string[] args)
    {
        if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase))) return "uninstall";
        if (args.Any(a => a.Equals("--verify", StringComparison.OrdinalIgnoreCase))) return "verify";
        if (args.Any(a => a.Equals("--upgrade", StringComparison.OrdinalIgnoreCase))) return "upgrade";
        return "install";
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase) && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return args[i + 1];
        }
        return null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("NativeShellKit Developer Kit Installer");
        Console.WriteLine("  --gui                     Launch the interactive graphical Setup Wizard");
        Console.WriteLine("  --gui-self-test <dir>     Run interactive GUI wizard self-test with PNG screenshots");
        Console.WriteLine("  --install                 Install components headlessly via CLI");
        Console.WriteLine("  --upgrade                 Upgrade existing installation preserving user data");
        Console.WriteLine("  --verify                  Verify installed files, SHA-256 sums, and NuGet archives");
        Console.WriteLine("  --uninstall               Remove managed Developer Kit components");
        Console.WriteLine("  --components <list>       packages,cli,templates,samples,docs,register");
        Console.WriteLine("  --install-dir <dir>       Target installation directory");
        Console.WriteLine("  --payload-dir <dir>       Source payload directory");
        Console.WriteLine("  --skip-dotnet-registration Skip modifying user NuGet/template config");
    }
}
