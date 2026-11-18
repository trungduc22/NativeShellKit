using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Notifications;
using NativeShellKit.Notifications;
using NativeShellKit.Notifications.History;
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
    private sealed class RecordingNativePresenter(bool isRegistered = true) : INativeNotificationPresenter
    {
        public bool IsRegistered { get; } = isRegistered;
        public List<(string AppId, string Id, string Args)> Presented { get; } = [];

        public void Present(string sourceAppId, AppNotificationRequest request, string resolvedNotificationId, string launchArguments) =>
            Presented.Add((sourceAppId, resolvedNotificationId, launchArguments));
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

        // 1. First notification succeeds & presents natively
        var res1 = await service.PublishAsync(new AppNotificationRequest("Build", "Done", NotificationSeverity.Success, DeduplicationKey: "k1"));
        Assert.True(res1.Persisted && res1.PresentedInApp && res1.PresentedNatively);
        Assert.Null(res1.SuppressedReason);

        // 2. Duplicate within window is persisted to history but suppressed for toast/banner
        var resDup = await service.PublishAsync(new AppNotificationRequest("Build", "Done again", NotificationSeverity.Info, DeduplicationKey: "k1"));
        Assert.True(resDup.Persisted);
        Assert.False(resDup.PresentedNatively);
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
        tray.RequestFullExit();
        Assert.True(exitRaised);
        Assert.False(tray.HandleWindowClosing());
        Assert.Contains(Win32Constants.NIM_DELETE, fake.ShellNotifyCalls);
    }
}
