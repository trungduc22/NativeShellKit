using System.Runtime.InteropServices;

namespace NativeShellKit.Windows.Interop;

public interface IWin32Interop
{
    bool ShowWindow(nint hwnd, int nCmdShow);
    bool SetForegroundWindow(nint hwnd);
    bool IsIconic(nint hwnd);
    uint GetDpiForWindow(nint hwnd);
    uint RegisterWindowMessage(string messageName);
    nint FindWindow(string className, string? windowName);
    bool GetCursorPos(out Win32Point point);
    nint CreatePopupMenu();
    bool AppendMenu(nint hMenu, uint flags, nuint itemId, string? text);
    uint TrackPopupMenu(nint hMenu, uint flags, int x, int y, nint hwnd);
    bool DestroyMenu(nint hMenu);
    nint LoadImage(string filePath, uint type, int width, int height, uint flags);
    bool DestroyIcon(nint hIcon);
    bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint subclassId);
    bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint subclassId);
    nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam);
    string? GetKnownFolderPath(Guid folderId);
}

public delegate nint SubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData);

[StructLayout(LayoutKind.Sequential)]
public struct Win32Point
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct NotifyIconData
{
    public int cbSize;
    public nint hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public nint hIcon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string szTip;
    public uint dwState;
    public uint dwStateMask;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string szInfo;
    public uint uTimeoutOrVersion;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string szInfoTitle;
    public uint dwInfoFlags;
    public Guid guidItem;
    public nint hBalloonIcon;
}

public static class Win32Constants
{
    public const int SW_HIDE = 0;
    public const int SW_SHOWNORMAL = 1;
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;

    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;
    public const uint NIM_SETVERSION = 0x00000004;

    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;
    public const uint NIF_GUID = 0x00000020;

    public const uint NOTIFYICON_VERSION_4 = 4;

    public const uint WM_APP = 0x8000;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_CONTEXTMENU = 0x007B;

    public const uint IMAGE_ICON = 1;
    public const uint LR_LOADFROMFILE = 0x00000010;
    public const uint LR_DEFAULTSIZE = 0x00000040;

    public const uint MF_STRING = 0x00000000;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint MF_GRAYED = 0x00000001;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_RIGHTBUTTON = 0x0002;
}

public sealed class Win32Interop : IWin32Interop
{
    public static Win32Interop Default { get; } = new();

    public bool ShowWindow(nint hwnd, int nCmdShow) => NativeShowWindow(hwnd, nCmdShow);
    public bool SetForegroundWindow(nint hwnd) => NativeSetForegroundWindow(hwnd);
    public bool IsIconic(nint hwnd) => NativeIsIconic(hwnd);
    public uint GetDpiForWindow(nint hwnd) => NativeGetDpiForWindow(hwnd);
    public uint RegisterWindowMessage(string messageName) => NativeRegisterWindowMessageW(messageName);
    public nint FindWindow(string className, string? windowName) => NativeFindWindowW(className, windowName);
    public bool GetCursorPos(out Win32Point point) => NativeGetCursorPos(out point);
    public nint CreatePopupMenu() => NativeCreatePopupMenu();
    public bool AppendMenu(nint hMenu, uint flags, nuint itemId, string? text) => NativeAppendMenuW(hMenu, flags, itemId, text);
    public uint TrackPopupMenu(nint hMenu, uint flags, int x, int y, nint hwnd) => NativeTrackPopupMenu(hMenu, flags, x, y, 0, hwnd, nint.Zero);
    public bool DestroyMenu(nint hMenu) => NativeDestroyMenu(hMenu);
    public nint LoadImage(string filePath, uint type, int width, int height, uint flags) => NativeLoadImageW(nint.Zero, filePath, type, width, height, flags);
    public bool DestroyIcon(nint hIcon) => NativeDestroyIcon(hIcon);
    public bool ShellNotifyIcon(uint message, ref NotifyIconData data) => NativeShell_NotifyIconW(message, ref data);
    public bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint subclassId) => NativeSetWindowSubclass(hwnd, proc, subclassId, nint.Zero);
    public bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint subclassId) => NativeRemoveWindowSubclass(hwnd, proc, subclassId);
    public nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam) => NativeDefSubclassProc(hwnd, msg, wParam, lParam);

    public string? GetKnownFolderPath(Guid folderId)
    {
        int hr = NativeSHGetKnownFolderPath(folderId, 0, nint.Zero, out nint pPath);
        if (hr != 0 || pPath == nint.Zero)
            return null;

        try
        {
            return Marshal.PtrToStringUni(pPath);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pPath);
        }
    }

    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeSetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "IsIconic")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeIsIconic(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    private static extern uint NativeGetDpiForWindow(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    private static extern uint NativeRegisterWindowMessageW(string lpString);

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    private static extern nint NativeFindWindowW(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeGetCursorPos(out Win32Point lpPoint);

    [DllImport("user32.dll", EntryPoint = "CreatePopupMenu")]
    private static extern nint NativeCreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeAppendMenuW(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", EntryPoint = "TrackPopupMenu")]
    private static extern uint NativeTrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

    [DllImport("user32.dll", EntryPoint = "DestroyMenu")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeDestroyMenu(nint hMenu);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)]
    private static extern nint NativeLoadImageW(nint hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", EntryPoint = "DestroyIcon")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeDestroyIcon(nint hIcon);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeShell_NotifyIconW(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("shell32.dll", EntryPoint = "SHGetKnownFolderPath")]
    private static extern int NativeSHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, nint hToken, out nint ppszPath);

    [DllImport("comctl32.dll", EntryPoint = "SetWindowSubclass")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeSetWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, nint dwRefData);

    [DllImport("comctl32.dll", EntryPoint = "RemoveWindowSubclass")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeRemoveWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll", EntryPoint = "DefSubclassProc")]
    private static extern nint NativeDefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);
}
