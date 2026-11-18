using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NativeShellKit.Installer;

public sealed class DeveloperKitInstallerWizardForm : Form
{
    private enum WizardPage
    {
        Welcome = 0,
        Directory = 1,
        Components = 2,
        Progress = 3,
        Completed = 4
    }

    private readonly string _payloadDir;
    private readonly bool _skipDotNetRegistration;
    private readonly string? _selfTestScreenshotsDir;

    private WizardPage _currentPage = WizardPage.Welcome;
    private Panel _pageHost = null!;
    private Panel _welcomePage = null!;
    private Panel _directoryPage = null!;
    private Panel _componentsPage = null!;
    private Panel _progressPage = null!;
    private Panel _completedPage = null!;

    private Label _headerTitle = null!;
    private Label _headerSubtitle = null!;
    private TextBox _installDirBox = null!;
    private Label _existingStatusLabel = null!;
    private Button _uninstallButton = null!;

    private CheckBox _chkPackages = null!;
    private CheckBox _chkCliTemplates = null!;
    private CheckBox _chkSamples = null!;
    private CheckBox _chkDocs = null!;
    private CheckBox _chkRegister = null!;

    private ProgressBar _progressBar = null!;
    private Label _progressStatusLabel = null!;
    private ListBox _progressLogList = null!;
    private Label _completedSummaryLabel = null!;

    private Button _backButton = null!;
    private Button _nextButton = null!;
    private Button _cancelButton = null!;

    public DeveloperKitInstallerWizardForm(
        string initialInstallDir,
        string payloadDir,
        bool skipDotNetRegistration,
        string? selfTestScreenshotsDir = null)
    {
        _payloadDir = payloadDir;
        _skipDotNetRegistration = skipDotNetRegistration;
        _selfTestScreenshotsDir = selfTestScreenshotsDir;

        Text = "NativeShellKit Developer Kit Setup (0.1.0-alpha.1)";
        Size = new Size(760, 520);
        MinimumSize = new Size(680, 480);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(248, 250, 252);
        Font = new Font("Segoe UI", 9.5F);

        BuildLayout(initialInstallDir);
        ShowPage(WizardPage.Welcome);

        if (!string.IsNullOrWhiteSpace(_selfTestScreenshotsDir))
        {
            Shown += OnSelfTestShownAsync;
        }
    }

    public int SelfTestExitCode { get; private set; } = -1;

    private void BuildLayout(string initialInstallDir)
    {
        var banner = new Panel
        {
            Dock = DockStyle.Top,
            Height = 84,
            BackColor = Color.FromArgb(15, 23, 42),
            Padding = new Padding(24, 16, 24, 12)
        };

        _headerTitle = new Label
        {
            Text = "NativeShellKit Developer Kit",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 14)
        };

        _headerSubtitle = new Label
        {
            Text = "Windows 10/11 (x64) Native WinUI 3 SDK • Pre-release 0.1.0-alpha.1",
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Segoe UI", 9.5F),
            AutoSize = true,
            Location = new Point(24, 46)
        };
        banner.Controls.Add(_headerTitle);
        banner.Controls.Add(_headerSubtitle);

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 64,
            BackColor = Color.FromArgb(241, 245, 249),
            Padding = new Padding(24, 14, 24, 14)
        };

        _backButton = new Button { Text = "< Back", Width = 96, Height = 34, Location = new Point(406, 15) };
        _nextButton = new Button
        {
            Text = "Next >",
            Width = 112,
            Height = 34,
            Location = new Point(512, 15),
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _nextButton.FlatAppearance.BorderSize = 0;
        _cancelButton = new Button { Text = "Cancel", Width = 96, Height = 34, Location = new Point(634, 15) };

        _backButton.Click += (_, _) => OnBackClicked();
        _nextButton.Click += async (_, _) => await OnNextClickedAsync();
        _cancelButton.Click += (_, _) => Close();

        footer.Controls.AddRange([_backButton, _nextButton, _cancelButton]);

        _pageHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 22, 28, 16)
        };

        _welcomePage = BuildWelcomePage();
        _directoryPage = BuildDirectoryPage(initialInstallDir);
        _componentsPage = BuildComponentsPage();
        _progressPage = BuildProgressPage();
        _completedPage = BuildCompletedPage();

        _pageHost.Controls.AddRange([_welcomePage, _directoryPage, _componentsPage, _progressPage, _completedPage]);

        Controls.Add(_pageHost);
        Controls.Add(footer);
        Controls.Add(banner);
        DisableMnemonics(this);
    }

    private static void DisableMnemonics(Control root)
    {
        if (root is Label lbl) lbl.UseMnemonic = false;
        else if (root is CheckBox chk) chk.UseMnemonic = false;
        foreach (Control child in root.Controls)
            DisableMnemonics(child);
    }

    private Panel BuildWelcomePage()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label
        {
            Text = "Welcome to NativeShellKit Developer Kit Setup",
            Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            AutoSize = true,
            Location = new Point(4, 4)
        });

        panel.Controls.Add(new Label
        {
            Text =
                "This wizard installs the local NativeShellKit development environment:\n\n" +
                "  • Signed/checksum-verified local NuGet package feed (13 packages, 0.1.0-alpha.1)\n" +
                "  • nsk developer CLI & installable dotnet new nativeshellkit templates\n" +
                "  • Three WinUI 3 reference samples (MinimalNativeApp, BackgroundCompanionApp, UnifiedHostDemo)\n" +
                "  • Architecture, versioning, and per-app migration guides\n\n" +
                "Important End-User Note:\n" +
                "Applications compiled with NativeShellKit bundle their own dependencies (WindowsAppSDKSelfContained) " +
                "and DO NOT require this Developer Kit to be installed on end-user machines.",
            MaximumSize = new Size(660, 0),
            AutoSize = true,
            ForeColor = Color.FromArgb(51, 65, 85),
            Location = new Point(4, 48)
        });
        return panel;
    }

    private Panel BuildDirectoryPage(string initialInstallDir)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label
        {
            Text = "Choose Installation Location & Action",
            Font = new Font("Segoe UI Semibold", 12.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            AutoSize = true,
            Location = new Point(4, 4)
        });

        panel.Controls.Add(new Label
        {
            Text = "Select a per-user destination folder for the Developer Kit. Existing user application data in %LOCALAPPDATA%\\NativeShellKit is never modified.",
            MaximumSize = new Size(660, 0),
            AutoSize = true,
            ForeColor = Color.FromArgb(71, 85, 105),
            Location = new Point(4, 38)
        });

        _installDirBox = new TextBox
        {
            Text = initialInstallDir,
            Width = 540,
            Location = new Point(4, 92)
        };
        _installDirBox.TextChanged += (_, _) => RefreshExistingState();

        var browseBtn = new Button
        {
            Text = "Browse...",
            Width = 104,
            Height = 28,
            Location = new Point(554, 90)
        };
        browseBtn.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = _installDirBox.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _installDirBox.Text = dialog.SelectedPath;
        };

        _existingStatusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(30, 64, 175),
            Location = new Point(4, 134)
        };

        _uninstallButton = new Button
        {
            Text = "Uninstall Existing Developer Kit",
            Width = 220,
            Height = 32,
            Location = new Point(4, 170)
        };
        _uninstallButton.Click += (_, _) => RunDirectUninstall();

        panel.Controls.AddRange([_installDirBox, browseBtn, _existingStatusLabel, _uninstallButton]);
        RefreshExistingState();
        return panel;
    }

    private Panel BuildComponentsPage()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label
        {
            Text = "Select Components to Install",
            Font = new Font("Segoe UI Semibold", 12.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            AutoSize = true,
            Location = new Point(4, 4)
        });

        _chkPackages = new CheckBox
        {
            Text = "Local NuGet Package Feed (13 NativeShellKit packages + SHA256SUMS.txt) [Required]",
            Checked = true,
            Enabled = false,
            AutoSize = true,
            Location = new Point(8, 48)
        };
        _chkCliTemplates = new CheckBox
        {
            Text = "Developer CLI (nsk.exe) & dotnet new nativeshellkit Templates",
            Checked = true,
            AutoSize = true,
            Location = new Point(8, 84)
        };
        _chkSamples = new CheckBox
        {
            Text = "Reference Sample Applications (MinimalNativeApp, BackgroundCompanionApp, UnifiedHostDemo)",
            Checked = true,
            AutoSize = true,
            Location = new Point(8, 120)
        };
        _chkDocs = new CheckBox
        {
            Text = "Architecture, Versioning & Migration Guides (docs/)",
            Checked = true,
            AutoSize = true,
            Location = new Point(8, 156)
        };
        _chkRegister = new CheckBox
        {
            Text = "Register NativeShellKitLocalFeed & Template in User .NET SDK Config",
            Checked = !_skipDotNetRegistration,
            Enabled = !_skipDotNetRegistration,
            AutoSize = true,
            Location = new Point(8, 192)
        };

        panel.Controls.AddRange([_chkPackages, _chkCliTemplates, _chkSamples, _chkDocs, _chkRegister]);
        return panel;
    }

    private Panel BuildProgressPage()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label
        {
            Text = "Installing NativeShellKit Developer Kit...",
            Font = new Font("Segoe UI Semibold", 12.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            AutoSize = true,
            Location = new Point(4, 4)
        });

        _progressStatusLabel = new Label
        {
            Text = "Preparing installation...",
            AutoSize = true,
            ForeColor = Color.FromArgb(51, 65, 85),
            Location = new Point(4, 40)
        };

        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Width = 656,
            Height = 24,
            Location = new Point(4, 70)
        };

        _progressLogList = new ListBox
        {
            Width = 656,
            Height = 210,
            Location = new Point(4, 108)
        };

        panel.Controls.AddRange([_progressStatusLabel, _progressBar, _progressLogList]);
        return panel;
    }

    private Panel BuildCompletedPage()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label
        {
            Text = "Developer Kit Operation Completed Successfully",
            Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
            ForeColor = Color.FromArgb(4, 120, 87),
            AutoSize = true,
            Location = new Point(4, 8)
        });

        _completedSummaryLabel = new Label
        {
            MaximumSize = new Size(650, 0),
            AutoSize = true,
            ForeColor = Color.FromArgb(51, 65, 85),
            Location = new Point(4, 52)
        };

        panel.Controls.Add(_completedSummaryLabel);
        return panel;
    }

    private void RefreshExistingState()
    {
        bool exists = InstallerEngine.IsAlreadyInstalled(_installDirBox.Text.Trim());
        _existingStatusLabel.Text = exists
            ? "Mode detected: UPGRADE existing Developer Kit installation in this directory."
            : "Mode detected: FRESH installation into this directory.";
        _uninstallButton.Visible = exists;
    }

    private void ShowPage(WizardPage page)
    {
        _currentPage = page;
        _welcomePage.Visible = page == WizardPage.Welcome;
        _directoryPage.Visible = page == WizardPage.Directory;
        _componentsPage.Visible = page == WizardPage.Components;
        _progressPage.Visible = page == WizardPage.Progress;
        _completedPage.Visible = page == WizardPage.Completed;

        _backButton.Enabled = page is WizardPage.Directory or WizardPage.Components;
        _cancelButton.Enabled = page != WizardPage.Progress;
        _nextButton.Enabled = page != WizardPage.Progress;

        bool isUpgrade = InstallerEngine.IsAlreadyInstalled(_installDirBox.Text.Trim());
        _nextButton.Text = page switch
        {
            WizardPage.Components => isUpgrade ? "Upgrade" : "Install",
            WizardPage.Completed => "Finish",
            _ => "Next >"
        };

        _headerSubtitle.Text = page switch
        {
            WizardPage.Welcome => "Step 1 of 4 • Welcome & Architecture Overview",
            WizardPage.Directory => "Step 2 of 4 • Installation Directory & Upgrade Detection",
            WizardPage.Components => "Step 3 of 4 • Component Selection",
            WizardPage.Progress => "Installing & Verifying Checksums...",
            WizardPage.Completed => "Setup Verified Successfully",
            _ => _headerSubtitle.Text
        };
    }

    private void OnBackClicked()
    {
        if (_currentPage == WizardPage.Directory) ShowPage(WizardPage.Welcome);
        else if (_currentPage == WizardPage.Components) ShowPage(WizardPage.Directory);
    }

    private async Task OnNextClickedAsync()
    {
        switch (_currentPage)
        {
            case WizardPage.Welcome:
                ShowPage(WizardPage.Directory);
                break;
            case WizardPage.Directory:
                if (string.IsNullOrWhiteSpace(_installDirBox.Text))
                {
                    MessageBox.Show(this, "Please specify a valid installation directory.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                ShowPage(WizardPage.Components);
                break;
            case WizardPage.Components:
                await ExecuteInstallAsync();
                break;
            case WizardPage.Completed:
                DialogResult = DialogResult.OK;
                Close();
                break;
        }
    }

    private DeveloperKitComponents CollectSelectedComponents()
    {
        var comps = DeveloperKitComponents.Packages;
        if (_chkCliTemplates.Checked) comps |= DeveloperKitComponents.CliAndTemplates;
        if (_chkSamples.Checked) comps |= DeveloperKitComponents.Samples;
        if (_chkDocs.Checked) comps |= DeveloperKitComponents.Documentation;
        if (_chkRegister.Checked && !_skipDotNetRegistration) comps |= DeveloperKitComponents.DotNetRegistration;
        return comps;
    }

    private async Task ExecuteInstallAsync()
    {
        string installDir = _installDirBox.Text.Trim();
        bool isUpgrade = InstallerEngine.IsAlreadyInstalled(installDir);
        var components = CollectSelectedComponents();

        _progressBar.Value = 0;
        _progressLogList.Items.Clear();
        ShowPage(WizardPage.Progress);

        InstallerOperationResult result = await Task.Run(() =>
        {
            var res = InstallerEngine.InstallOrUpgrade(_payloadDir, installDir, isUpgrade, components, (pct, msg) =>
            {
                BeginInvoke(() =>
                {
                    _progressBar.Value = Math.Clamp(pct, 0, 100);
                    _progressStatusLabel.Text = msg;
                    _progressLogList.Items.Add($"[{pct}%] {msg}");
                });
            });

            if (!res.Success)
                return res;

            var verifyRes = InstallerEngine.Verify(installDir);
            BeginInvoke(() => _progressLogList.Items.Add(verifyRes.Message));
            return verifyRes;
        });

        if (!result.Success)
        {
            _progressStatusLabel.Text = $"Error: {result.Message}";
            if (string.IsNullOrWhiteSpace(_selfTestScreenshotsDir))
            {
                MessageBox.Show(this, result.Message, "Installation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            ShowPage(WizardPage.Components);
            return;
        }

        _completedSummaryLabel.Text =
            $"Destination: {installDir}\n" +
            $"Operation: {(isUpgrade ? "Upgrade" : "Fresh Install")}\n" +
            $"Verification: {result.Message}\n\n" +
            "End-user applications packaged from this SDK remain fully self-contained and do not depend on this Developer Kit folder.";

        ShowPage(WizardPage.Completed);
        RefreshExistingState();
    }

    private void RunDirectUninstall()
    {
        string installDir = _installDirBox.Text.Trim();
        var confirm = MessageBox.Show(
            this,
            $"Remove NativeShellKit Developer Kit from:\n{installDir}\n\nUser application data will be preserved.",
            "Confirm Uninstall",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
            return;

        var res = InstallerEngine.Uninstall(installDir, _skipDotNetRegistration);
        RefreshExistingState();
        MessageBox.Show(this, res.Message, Text, MessageBoxButtons.OK,
            res.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
    }

    private async void OnSelfTestShownAsync(object? sender, EventArgs e)
    {
        try
        {
            string shotDir = _selfTestScreenshotsDir!;
            Directory.CreateDirectory(shotDir);

            await CaptureStepAsync(Path.Combine(shotDir, "installer-1-welcome.png"));

            ShowPage(WizardPage.Directory);
            await CaptureStepAsync(Path.Combine(shotDir, "installer-2-directory.png"));

            ShowPage(WizardPage.Components);
            await CaptureStepAsync(Path.Combine(shotDir, "installer-3-components.png"));

            // Capture progress mid-install cleanly by running install + saving progress screenshot + completed screenshot
            var installTask = ExecuteInstallAsync();
            await Task.Delay(150);
            if (_currentPage == WizardPage.Progress)
                CaptureFormPng(Path.Combine(shotDir, "installer-4-progress.png"));
            await installTask;

            if (_currentPage != WizardPage.Completed)
            {
                SelfTestExitCode = 10;
                Close();
                return;
            }

            if (!File.Exists(Path.Combine(shotDir, "installer-4-progress.png")))
                CaptureFormPng(Path.Combine(shotDir, "installer-4-progress.png"));

            await CaptureStepAsync(Path.Combine(shotDir, "installer-5-completed.png"));

            // Exercise upgrade detection + clean uninstall
            ShowPage(WizardPage.Directory);
            await ExecuteInstallAsync();
            if (_currentPage != WizardPage.Completed)
            {
                SelfTestExitCode = 11;
                Close();
                return;
            }

            var uninst = InstallerEngine.Uninstall(_installDirBox.Text.Trim(), skipRegistration: true);
            SelfTestExitCode = uninst.Success ? 0 : uninst.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"GUI installer self-test failed: {ex}");
            SelfTestExitCode = 12;
        }
        finally
        {
            Close();
        }
    }

    private async Task CaptureStepAsync(string outputPath)
    {
        Application.DoEvents();
        await Task.Delay(250);
        Application.DoEvents();
        CaptureFormPng(outputPath);
    }

    private void CaptureFormPng(string outputPath)
    {
        if (!GetWindowRect(Handle, out var rect))
            return;

        int width = Math.Max(1, rect.Right - rect.Left);
        int height = Math.Max(1, rect.Bottom - rect.Top);
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        nint hdc = g.GetHdc();
        try
        {
            const uint PW_RENDERFULLCONTENT = 0x00000002;
            if (!PrintWindow(Handle, hdc, PW_RENDERFULLCONTENT))
                PrintWindow(Handle, hdc, 0);
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }

        bmp.Save(outputPath, ImageFormat.Png);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);
}
