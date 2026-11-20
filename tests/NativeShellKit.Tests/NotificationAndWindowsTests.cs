using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Notifications;
using NativeShellKit.Abstractions.Settings;
using NativeShellKit.Abstractions.Tray;
using NativeShellKit.Diagnostics.Logging;
using NativeShellKit.Hosting.Hosting;
using NativeShellKit.Notifications;
using NativeShellKit.Notifications.History;
using NativeShellKit.Notifications.Presenters;
using NativeShellKit.Tray;
using NativeShellKit.Windows.Interop;
using NativeShellKit.Windows.Platform;
using NativeShellKit.Windows.Startup;
using NativeShellKit.Windows.Storage;
using NativeShellKit.Windows.SystemServices;
using Xunit;

namespace NativeShellKit.Tests;

public sealed class NotificationAndWindowsTests
{
    private sealed class RecordingNativePresenter(
        NativeNotificationRegistrationStatus regStatus = NativeNotificationRegistrationStatus.Registered,
        NativeNotificationSubmissionStatus subStatus = NativeNotificationSubmissionStatus.SubmittedToPlatform,
        string? errorMsg = null) : INativeNotificationPresenter
    {
        public NativeNotificationRegistrationStatus RegistrationStatus { get; } = regStatus;
        public bool IsRegistered => RegistrationStatus == NativeNotificationRegistrationStatus.Registered;
        public string? RegistrationError { get; } = regStatus == NativeNotificationRegistrationStatus.Failed ? errorMsg : null;
        public List<(string AppId, string Id, string Args)> Presented { get; } = [];

        public NativeNotificationSubmissionResult Present(
            string sourceAppId,
            AppNotificationRequest request,
            string resolvedNotificationId,
            string launchArguments)
        {
            Presented.Add((sourceAppId, resolvedNotificationId, launchArguments));
            bool ok = subStatus == NativeNotificationSubmissionStatus.SubmittedToPlatform;
            return new NativeNotificationSubmissionResult(
                subStatus,
                SubmittedToPlatform: ok,
                PlatformNotificationTag: ok ? resolvedNotificationId : null,
                ErrorMessage: errorMsg);
        }
    }

    private sealed class FakeTrayInterop : IWin32Interop
    {
        public List<uint> ShellNotifyCalls { get; } = [];
        public SubclassProc? Subclass { get; private set; }
        public nuint SubclassId { get; private set; }
        public uint TaskbarMsgId { get; } = 0xC123;
        public uint NextSelectedMenuCommand { get; set; }
        public List<int> ShowWindowCalls { get; } = [];

        public bool ShowWindow(nint hwnd, int nCmdShow) { ShowWindowCalls.Add(nCmdShow); return true; }
        public bool SetForegroundWindow(nint hwnd) => true;
        public bool IsIconic(nint hwnd) => false;
        public uint GetDpiForWindow(nint hwnd) => 96;
        public uint RegisterWindowMessage(string messageName) => messageName == "TaskbarCreated" ? TaskbarMsgId : 0xC999;
        public nint FindWindow(string className, string? windowName) => nint.Zero;
        public bool GetCursorPos(out Win32Point point) { point = new Win32Point { X = 10, Y = 20 }; return true; }
        public nint CreatePopupMenu() => new(0x555);
        public bool AppendMenu(nint hMenu, uint flags, nuint itemId, string? text) => true;
        public uint TrackPopupMenu(nint hMenu, uint flags, int x, int y, nint hwnd) => NextSelectedMenuCommand;
        public bool DestroyMenu(nint hMenu) => true;
        public nint LoadImage(string filePath, uint type, int width, int height, uint flags) => nint.Zero;
        public bool DestroyIcon(nint hIcon) => true;
        public bool ShellNotifyIcon(uint message, ref NotifyIconData data) { ShellNotifyCalls.Add(message); return true; }
        public bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint subclassId) { Subclass = proc; SubclassId = subclassId; return true; }
        public bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint subclassId) => true;
        public nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam) => nint.Zero;
        public string? GetKnownFolderPath(Guid folderId) => null;
    }

    [Fact]
    public async Task NotificationService_EnforcesPersistenceDedupRateQuietHoursAndActivationIsolation()
    {
        using var tmp = new TempRootScope();
        var identity = new AppIdentity("notifyapp", "Notify App", baseDataRoot: tmp.RootPath);
        var history = new JsonNotificationHistoryStore(identity);
        var presenter = new RecordingNativePresenter();
        var now = new DateTimeOffset(2026, 11, 18, 14, 0, 0, TimeSpan.Zero);

        var service = new UnifiedNotificationService(identity, history, presenter, clock: () => now);
        service.Preferences.MaxNotificationsPerMinute = 2;

        // 1. First notification submits to platform; VisualDisplayConfirmed stays false unless interactively confirmed
        var res1 = await service.PublishAsync(new AppNotificationRequest("Build", "Done", NotificationSeverity.Success, DeduplicationKey: "k1"));
        Assert.True(res1.Persisted && res1.PresentedInApp && res1.PresentedNatively);
        Assert.Equal(NativeNotificationSubmissionStatus.SubmittedToPlatform, res1.NativeSubmissionStatus);
        Assert.False(res1.VisualDisplayConfirmed);
        Assert.Null(res1.SuppressedReason);

        // 2. Duplicate within window is persisted to history but suppressed for toast/banner
        var resDup = await service.PublishAsync(new AppNotificationRequest("Build", "Done again", NotificationSeverity.Info, DeduplicationKey: "k1"));
        Assert.True(resDup.Persisted);
        Assert.False(resDup.PresentedNatively);
        Assert.Equal(NativeNotificationSubmissionStatus.Suppressed, resDup.NativeSubmissionStatus);
        Assert.Equal("Deduplicated", resDup.SuppressedReason);

        // 3. Second distinct notification uses up MaxNotificationsPerMinute=2; third hits RateLimited
        var res2 = await service.PublishAsync(new AppNotificationRequest("Sync", "OK", NotificationSeverity.Info, DeduplicationKey: "k2"));
        Assert.Null(res2.SuppressedReason);
        var resRate = await service.PublishAsync(new AppNotificationRequest("Sync 2", "Too fast", NotificationSeverity.Info, DeduplicationKey: "k3"));
        Assert.Equal("RateLimited", resRate.SuppressedReason);

        // 4. Advance clock past rate window and test QuietHoursActive & CategoryDisabled
        now = now.AddMinutes(2);
        service.Preferences.DisabledCategories.Add("MutedCat");
        var resCat = await service.PublishAsync(new AppNotificationRequest("Muted", "Skipped", Category: "MutedCat", DeduplicationKey: "k4"));
        Assert.Equal("CategoryDisabled", resCat.SuppressedReason);

        service.Preferences.QuietHoursEnabled = true;
        int localHour = now.LocalDateTime.Hour;
        service.Preferences.QuietHoursStartHour = localHour;
        service.Preferences.QuietHoursEndHour = (localHour + 1) % 24;
        var resQuiet = await service.PublishAsync(new AppNotificationRequest("Night", "Shh", DeduplicationKey: "k5"));
        Assert.Equal("QuietHoursActive", resQuiet.SuppressedReason);

        // 5. Reject cross-app activation & accept matching-app activation marking item read
        Assert.False(await service.TryHandleActivationAsync($"appId=otherapp&notificationId={res1.NotificationId}&action=open"));
        NotificationActivationArgs? activated = null;
        service.RegisterActionHandler("open", args => { activated = args; return Task.CompletedTask; });
        var argsStr = service.BuildActivationArguments(res1.NotificationId, "open", "overview");
        Assert.True(await service.TryHandleActivationAsync(argsStr));
        Assert.Equal("overview", activated?.DestinationRoute);

        var recent = await service.GetHistoryAsync(20);
        Assert.Contains(recent, r => r.NotificationId == res1.NotificationId && r.IsRead);

        await service.ClearHistoryAsync();
        Assert.Empty(await service.GetHistoryAsync(20));
    }

    [Fact]
    public async Task NotificationService_PreservesPlatformFailuresAndSupervisesActivationErrors()
    {
        using var tmp = new TempRootScope();
        var identity = new AppIdentity("notifyfail", "Notify Fail", baseDataRoot: tmp.RootPath);
        var history = new JsonNotificationHistoryStore(identity);
        var logger = new RedactingFileLogger(identity);
        await using var supervisor = new BackgroundTaskSupervisor(logger);

        // Unregistered / failed presenter reports NotRegistered rather than faking success
        var failedPresenter = new RecordingNativePresenter(
            NativeNotificationRegistrationStatus.Failed,
            NativeNotificationSubmissionStatus.NotRegistered,
            "COM registration failed");
        var svcNotReg = new UnifiedNotificationService(identity, history, failedPresenter, taskSupervisor: supervisor, logger: logger);
        var notRegResult = await svcNotReg.PublishAsync(new AppNotificationRequest("Hello", "Body"));
        Assert.False(notRegResult.PresentedNatively);
        Assert.Equal(NativeNotificationSubmissionStatus.NotRegistered, notRegResult.NativeSubmissionStatus);
        Assert.Equal("COM registration failed", notRegResult.NativeErrorMessage);
        Assert.False(notRegResult.VisualDisplayConfirmed);

        // Registered presenter whose Show() throws reports PlatformFailure + error diagnostics
        var errorPresenter = new RecordingNativePresenter(
            NativeNotificationRegistrationStatus.Registered,
            NativeNotificationSubmissionStatus.PlatformFailure,
            "WinRT toast service returned 0x80070490");
        var svcError = new UnifiedNotificationService(identity, history, errorPresenter, taskSupervisor: supervisor, logger: logger);
        var errResult = await svcError.PublishAsync(new AppNotificationRequest("Alert", "Body"));
        Assert.False(errResult.PresentedNatively);
        Assert.Equal(NativeNotificationSubmissionStatus.PlatformFailure, errResult.NativeSubmissionStatus);
        Assert.Contains("0x80070490", errResult.NativeErrorMessage);

        // Supervised activation catches & reports handler errors via supervisor + ActivationFailed event
        svcError.RegisterActionHandler("boom", _ => throw new InvalidOperationException("handler-crash"));
        var activationFailedTcs = new TaskCompletionSource<Exception>();
        var supervisorFailedTcs = new TaskCompletionSource<string>();
        svcError.ActivationFailed += (_, tuple) => activationFailedTcs.TrySetResult(tuple.Error);
        supervisor.TaskFailed += (name, _) => supervisorFailedTcs.TrySetResult(name);

        var badArgs = svcError.BuildActivationArguments("n1", "boom");
        await svcError.DispatchPlatformActivationAsync(badArgs);

        var ex = await activationFailedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var taskName = await supervisorFailedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("handler-crash", ex.Message);
        Assert.Contains("notifyfail", taskName);
    }

    [Fact]
    public void WindowsAppNotificationPresenter_TracksRealRegistrationAndSubmissionStateSafely()
    {
        using var tmp = new TempRootScope();
        var identity = new AppIdentity("realpresenter", "Real Presenter", baseDataRoot: tmp.RootPath);
        var logger = new RedactingFileLogger(identity);

        using var presenter = new WindowsAppNotificationPresenter(identity, logger: logger);
        Assert.True(
            presenter.RegistrationStatus is NativeNotificationRegistrationStatus.Registered
                or NativeNotificationRegistrationStatus.Failed);

        var submission = presenter.Present(
            identity.AppId,
            new AppNotificationRequest("Test", "Diagnostics check"),
            "test-1",
            "appId=realpresenter&notificationId=test-1&action=open");

        if (presenter.IsRegistered)
        {
            Assert.True(
                submission.Status is NativeNotificationSubmissionStatus.SubmittedToPlatform
                    or NativeNotificationSubmissionStatus.PlatformFailure);
        }
        else
        {
            Assert.Equal(NativeNotificationSubmissionStatus.NotRegistered, submission.Status);
            Assert.False(submission.SubmittedToPlatform);
            Assert.False(string.IsNullOrWhiteSpace(presenter.RegistrationError));
        }
    }

    [Fact]
    public void WindowsIntegration_VersionFoldersStartupAndProtocol_WorkWithoutRegistryMutation()
    {
        using var tmp = new TempRootScope();
        var identity = new AppIdentity("winapp", "Win App", baseDataRoot: tmp.RootPath);

        var ver = new WindowsVersionService().GetCurrent();
        Assert.True(ver.Is64BitProcess);
        Assert.True(ver.BuildNumber >= 17763);

        var folders = new KnownFoldersService();
        var iso = folders.GetIsolatedAppDirectory(identity, "cache");
        Assert.True(Directory.Exists(iso));
        Assert.StartsWith(identity.DataDirectory, iso, StringComparison.OrdinalIgnoreCase);

        var startup = new WindowsStartupService(identity, new InMemoryStartupRegistryBackend());
        Assert.False(startup.IsEnabled());
        startup.Enable(@"C:\Apps\WinApp\WinApp.exe", startMinimized: true);
        Assert.True(startup.IsEnabled());
        Assert.Contains("--autostart --minimized", startup.GetRegisteredCommand());
        startup.Disable();
        Assert.False(startup.IsEnabled());

        Assert.True(ProtocolActivationService.TryParseActivationUri("winapp://open?tab=models&id=42", "winapp", out var action, out var query));
        Assert.Equal("open", action);
        Assert.Equal("models", query["tab"]);
        Assert.Equal("42", query["id"]);
        Assert.False(ProtocolActivationService.TryParseActivationUri("other://open", "winapp", out _, out _));
    }

    [Fact]
    public void TrayService_ManagesLifecycleExplorerRestartAndMenuCallbacks()
    {
        var identity = new AppIdentity("trayapp", "Tray App");
        var fake = new FakeTrayInterop();
        using var tray = new NativeTrayService(identity, fake);

        bool actionRan = false;
        tray.SetMenuItems([new TrayMenuItem(10, "Do Action", () => actionRan = true)]);

        var fakeHwnd = new nint(0x1234);
        Assert.True(tray.Initialize(fakeHwnd));
        Assert.True(tray.IsIconActive);
        Assert.Contains(Win32Constants.NIM_ADD, fake.ShellNotifyCalls);

        // Explorer restart recovery
        Assert.True(tray.RecoverAfterExplorerRestart());
        Assert.Equal(2, fake.ShellNotifyCalls.Count(c => c == Win32Constants.NIM_ADD));

        // Close minimizes to tray instead of closing when allowed
        Assert.True(tray.HandleWindowClosing());
        Assert.Contains(Win32Constants.SW_HIDE, fake.ShowWindowCalls);

        // Simulate context menu selection via WindowSubclassCallback
        fake.NextSelectedMenuCommand = 10;
        fake.Subclass?.Invoke(fakeHwnd, Win32Constants.WM_APP + 101, 0, (nint)Win32Constants.WM_RBUTTONUP, fake.SubclassId, 0);
        Assert.True(actionRan);

        bool exitRaised = false;
        tray.FullExitRequested += (_, _) => exitRaised = true;
        tray.UpdateTooltip("Tray App (Synced)");
        ITrayService trayContract = tray;
        trayContract.SetMenuItems([new NativeShellKit.Abstractions.Tray.TrayMenuDescriptor(11, "From Interface")]);
        trayContract.RequestFullExit();
        Assert.True(exitRaised);
        Assert.False(trayContract.HandleWindowClosing());
        Assert.Contains(Win32Constants.NIM_DELETE, fake.ShellNotifyCalls);
    }
}
