using System.Runtime.InteropServices;

namespace DesktopPanels.Services;

public sealed class TrayIcon : IDisposable
{
    const int WmContextMenu = 0x007B;
    const int WmRButtonUp = 0x0205;
    const int WmLButtonUp = 0x0202;
    const int WmNull = 0x0000;
    const int NimAdd = 0x00000000;
    const int NimDelete = 0x00000002;
    const int NimSetVersion = 0x00000004;
    const int NifMessage = 0x00000001;
    const int NifIcon = 0x00000002;
    const int NifTip = 0x00000004;
    const int NifShowTip = 0x00000080;
    const int NotifyIconVersion4 = 4;
    const int MfString = 0x0000;
    const int MfSeparator = 0x0800;
    const int MfChecked = 0x0008;
    const int TpmRightButton = 0x0002;
    const int TpmBottomAlign = 0x0020;
    const uint TpmReturnCmd = 0x0100;
    const uint WmUser = 0x0400;
    const int CallbackMessage = (int)(WmUser + 1);

    const int IdIcons = 1;
    const int IdFolder = 2;
    const int IdSlideshow = 3;
    const int IdClock = 4;
    const int IdWeather = 5;
    const int IdEdit = 6;
    const int IdIconsToggle = 7;
    const int IdQuit = 8;

    readonly WndProc _wndProc;
    IntPtr _hwnd;
    bool _added;

    public TrayIcon()
    {
        _wndProc = WindowProc;
    }

    public void Show()
    {
        var className = "DesktopPanels.Tray." + Environment.ProcessId;
        var wc = new WndClass
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            lpszClassName = className,
            hInstance = GetModuleHandle(null)
        };
        RegisterClass(ref wc);
        _hwnd = CreateWindowEx(0, className, "Desktop Panels", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, wc.hInstance, IntPtr.Zero);

        var iconPath = AppIcon.FilePath;
        var icon = File.Exists(iconPath) ? LoadImage(IntPtr.Zero, iconPath, 1, 0, 0, 0x0010) : IntPtr.Zero;
        if (icon == IntPtr.Zero)
        {
            icon = LoadIcon(IntPtr.Zero, new IntPtr(32512));
        }

        var data = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip | NifShowTip,
            uCallbackMessage = CallbackMessage,
            hIcon = icon,
            szTip = "Desktop Panels",
            uTimeoutOrVersion = NotifyIconVersion4
        };
        Shell_NotifyIcon(NimAdd, ref data);
        Shell_NotifyIcon(NimSetVersion, ref data);
        _added = true;
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = new NotifyIconData
            {
                cbSize = Marshal.SizeOf<NotifyIconData>(),
                hWnd = _hwnd,
                uID = 1
            };
            Shell_NotifyIcon(NimDelete, ref data);
            _added = false;
        }

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == CallbackMessage)
        {
            var mouse = (int)(lParam.ToInt64() & 0xFFFF);
            if (mouse is WmRButtonUp or WmContextMenu or WmLButtonUp)
            {
                ShowMenu();
            }

            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, MfString, IdIcons, "New shortcut panel");
        AppendMenu(menu, MfString, IdFolder, "New folder panel");
        AppendMenu(menu, MfString, IdSlideshow, "New slideshow panel");
        AppendMenu(menu, MfString, IdClock, "New clock");
        AppendMenu(menu, MfString, IdWeather, "New weather panel");
        AppendMenu(menu, MfSeparator, 0, null);
        AppendMenu(menu, MfString | (AppState.IsEditing ? MfChecked : 0), IdEdit, "Edit panels");
        AppendMenu(menu, MfString, IdIconsToggle, AppState.DesktopIconsHidden ? "Show desktop icons" : "Hide desktop icons");
        AppendMenu(menu, MfSeparator, 0, null);
        AppendMenu(menu, MfString, IdQuit, "Quit");

        GetCursorPos(out var point);
        SetForegroundWindow(_hwnd);
        var command = TrackPopupMenu(menu, TpmReturnCmd | TpmRightButton | TpmBottomAlign, point.X, point.Y, 0, _hwnd, IntPtr.Zero);
        PostMessage(_hwnd, WmNull, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);

        switch (command)
        {
            case IdIcons:
                PanelManager.AddPanel(Models.PanelKind.Icons);
                break;
            case IdFolder:
                PanelManager.AddPanel(Models.PanelKind.Folder);
                break;
            case IdSlideshow:
                PanelManager.AddPanel(Models.PanelKind.Slideshow);
                break;
            case IdClock:
                PanelManager.AddPanel(Models.PanelKind.Clock);
                break;
            case IdWeather:
                PanelManager.AddPanel(Models.PanelKind.Weather);
                break;
            case IdEdit:
                AppState.SetEditing(!AppState.IsEditing);
                break;
            case IdIconsToggle:
                if (AppState.DesktopIconsHidden)
                {
                    DesktopHost.ShowIcons();
                }
                else
                {
                    DesktopHost.HideIcons();
                }

                break;
            case IdQuit:
                PanelManager.Quit();
                break;
        }
    }

    delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WndClass
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern ushort RegisterClass(ref WndClass lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool Shell_NotifyIcon(int dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool AppendMenu(IntPtr hMenu, int uFlags, nint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out NativeWindow.PointNative lpPoint);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
