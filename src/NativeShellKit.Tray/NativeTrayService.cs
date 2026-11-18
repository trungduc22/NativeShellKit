using System.Runtime.InteropServices;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Windows.Interop;
using NativeShellKit.Windows.SystemServices;

namespace NativeShellKit.Tray;

public sealed record TrayMenuItem(
    uint Id,
    string Text,
    Action? Callback = null,
    bool IsSeparator = false,
    bool IsEnabled = true);

public sealed class NativeTrayService : IDisposable
{
    private const uint TrayCallbackMessage = Win32Constants.WM_APP + 101;
    private const nuint SubclassId = 4102;

    private readonly AppIdentity _identity;
    private readonly IWin32Interop _interop;
    private readonly WindowManagerService _windowManager;
    private readonly List<TrayMenuItem> _menuItems = [];
    private readonly SubclassProc _subclassDelegate;

    private nint _hwnd;
    private nint _hIcon;
    private uint _taskbarCreatedMessage;
    private bool _iconAdded;
    private bool _allowClose;
    private bool _disposed;
    private bool _useGuidFlag = true;

    public NativeTrayService(
        AppIdentity identity,
        IWin32Interop? interop = null,
        WindowManagerService? windowManager = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _interop = interop ?? Win32Interop.Default;
        _windowManager = windowManager ?? new WindowManagerService(_interop);
        _subclassDelegate = WindowSubclassCallback;
    }

    public bool IsIconActive => _iconAdded;
    public bool HasTaskbarShell => _interop.FindWindow("Shell_TrayWnd", null) != nint.Zero;
    public string? LastTrayError { get; private set; }
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public IReadOnlyList<TrayMenuItem> MenuItems => _menuItems;

    public event EventHandler? RestoreRequested;
    public event EventHandler? FullExitRequested;

    public void SetMenuItems(IEnumerable<TrayMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _menuItems.Clear();
        _menuItems.AddRange(items);
    }

    public bool Initialize(nint ownerHwnd, string? iconPath = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ownerHwnd == nint.Zero)
            return false;

        _hwnd = ownerHwnd;
        _taskbarCreatedMessage = _interop.RegisterWindowMessage("TaskbarCreated");

        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            _hIcon = _interop.LoadImage(
                iconPath,
                Win32Constants.IMAGE_ICON,
                16,
                16,
                Win32Constants.LR_LOADFROMFILE | Win32Constants.LR_DEFAULTSIZE);
        }

        _interop.SetWindowSubclass(_hwnd, _subclassDelegate, SubclassId);
        return AddOrUpdateTrayIcon();
    }

    public bool HandleWindowClosing()
    {
        if (_allowClose || !MinimizeToTrayOnClose)
            return false;

        HideToTray();
        return true;
    }

    public void HideToTray()
    {
        if (_hwnd != nint.Zero)
            _windowManager.Hide(_hwnd);
    }

    public void ShowFromTray()
    {
        if (_hwnd != nint.Zero)
            _windowManager.ShowAndRestore(_hwnd);
        RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestFullExit()
    {
        _allowClose = true;
        RemoveTrayIcon();
        FullExitRequested?.Invoke(this, EventArgs.Empty);
    }

    public bool RecoverAfterExplorerRestart()
    {
        if (_disposed || _hwnd == nint.Zero)
            return false;
        _iconAdded = false;
        return AddOrUpdateTrayIcon();
    }

    private bool AddOrUpdateTrayIcon()
    {
        var data = BuildNotifyIconData(_useGuidFlag);
        uint msg = _iconAdded ? Win32Constants.NIM_MODIFY : Win32Constants.NIM_ADD;
        bool ok = _interop.ShellNotifyIcon(msg, ref data);
        if (!ok && !_iconAdded && _useGuidFlag)
        {
            // Retry without NIF_GUID on unpackaged/relocated executables where Shell rejects stale GUID paths
            _useGuidFlag = false;
            data = BuildNotifyIconData(includeGuid: false);
            ok = _interop.ShellNotifyIcon(Win32Constants.NIM_ADD, ref data);
        }

        if (ok)
        {
            LastTrayError = null;
            if (!_iconAdded)
            {
                _iconAdded = true;
                data.uTimeoutOrVersion = Win32Constants.NOTIFYICON_VERSION_4;
                _interop.ShellNotifyIcon(Win32Constants.NIM_SETVERSION, ref data);
            }
        }
        else
        {
            int err = Marshal.GetLastWin32Error();
            LastTrayError = HasTaskbarShell
                ? $"Shell_NotifyIconW returned false (Win32Error=0x{err:X8})."
                : $"Shell_TrayWnd taskbar window is not available on current desktop (Win32Error=0x{err:X8}).";
        }
        return ok;
    }

    private void RemoveTrayIcon()
    {
        if (!_iconAdded || _hwnd == nint.Zero)
            return;

        var data = BuildNotifyIconData(_useGuidFlag);
        _interop.ShellNotifyIcon(Win32Constants.NIM_DELETE, ref data);
        _iconAdded = false;
    }

    private NotifyIconData BuildNotifyIconData(bool includeGuid) => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = Win32Constants.NIF_MESSAGE | Win32Constants.NIF_TIP |
                 (includeGuid ? Win32Constants.NIF_GUID : 0u) |
                 (_hIcon != nint.Zero ? Win32Constants.NIF_ICON : 0u),
        uCallbackMessage = TrayCallbackMessage,
        hIcon = _hIcon,
        szTip = Truncate(_identity.DisplayName, 127),
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
        guidItem = includeGuid ? _identity.TrayIconGuid : Guid.Empty
    };

    private nint WindowSubclassCallback(nint hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData)
    {
        if (_taskbarCreatedMessage != 0 && uMsg == _taskbarCreatedMessage)
        {
            RecoverAfterExplorerRestart();
        }
        else if (uMsg == TrayCallbackMessage)
        {
            uint eventId = (uint)(lParam.ToInt64() & 0xFFFF);
            if (eventId is Win32Constants.WM_LBUTTONUP or Win32Constants.WM_LBUTTONDBLCLK)
            {
                ShowFromTray();
            }
            else if (eventId is Win32Constants.WM_RBUTTONUP or Win32Constants.WM_CONTEXTMENU)
            {
                ShowContextMenu();
            }
        }
        return _interop.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        if (_hwnd == nint.Zero || _menuItems.Count == 0)
            return;

        nint menu = _interop.CreatePopupMenu();
        if (menu == nint.Zero)
            return;

        try
        {
            foreach (var item in _menuItems)
            {
                if (item.IsSeparator)
                {
                    _interop.AppendMenu(menu, Win32Constants.MF_SEPARATOR, 0, null);
                }
                else
                {
                    uint flags = Win32Constants.MF_STRING | (item.IsEnabled ? 0u : Win32Constants.MF_GRAYED);
                    _interop.AppendMenu(menu, flags, item.Id, item.Text);
                }
            }

            _interop.GetCursorPos(out var pt);
            _interop.SetForegroundWindow(_hwnd);
            uint selected = _interop.TrackPopupMenu(menu, Win32Constants.TPM_RETURNCMD | Win32Constants.TPM_RIGHTBUTTON, pt.X, pt.Y, _hwnd);
            if (selected != 0)
            {
                _menuItems.FirstOrDefault(m => m.Id == selected)?.Callback?.Invoke();
            }
        }
        finally
        {
            _interop.DestroyMenu(menu);
        }
    }

    private static string Truncate(string value, int maxLen) =>
        value.Length <= maxLen ? value : value[..maxLen];

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        RemoveTrayIcon();
        if (_hwnd != nint.Zero)
            _interop.RemoveWindowSubclass(_hwnd, _subclassDelegate, SubclassId);
        if (_hIcon != nint.Zero)
        {
            _interop.DestroyIcon(_hIcon);
            _hIcon = nint.Zero;
        }
    }
}
