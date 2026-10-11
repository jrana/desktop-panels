using System.Runtime.InteropServices;
using System.Text;

namespace DesktopPanels.Services;

public static class NativeWindow
{
    public const int GwlWndProc = -4;
    public const int GwlStyle = -16;
    public const int GwlExStyle = -20;
    public const int WsCaption = 0x00C00000;
    public const int WsThickFrame = 0x00040000;
    public const int WsBorder = 0x00800000;
    public const int WsDlgFrame = 0x00400000;
    public const int WsExWindowEdge = 0x00000100;
    public const int WsExClientEdge = 0x00000200;
    public const int WsExStaticEdge = 0x00020000;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExNoActivate = 0x08000000;
    public static readonly IntPtr HwndBottom = new(1);
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpFrameChanged = 0x0020;
    public const int DwmwColorNone = unchecked((int)0xFFFFFFFE);
    public const int DwmwaBorderColor = 34;
    public const int DwmwaWindowCornerPreference = 33;
    public const int DwmwcpRound = 2;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern nint GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern nint SetWindowLongPtr64(IntPtr hWnd, int nIndex, nint dwNewLong);

    public static nint GetWindowLong(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

    public static nint SetWindowLong(IntPtr hWnd, int nIndex, nint dwNewLong) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, (int)dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out PointNative lpPoint);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RectNative rect);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo info);

    public const uint GwHwndNext = 2;
    public const uint GwHwndPrev = 3;
    public const int WsExAppWindow = 0x00040000;
    const int DwmwaCloaked = 14;

    public static RectNative WorkArea(IntPtr hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, 2);
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return new RectNative { Right = 1920, Bottom = 1080 };
        }

        return info.rcWork;
    }

    public static bool CursorInside(IntPtr hwnd)
    {
        if (!GetCursorPos(out var point) || !GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        return point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;
    }

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    const uint WmNcCalcSize = 0x0083;
    static readonly Dictionary<IntPtr, nint> PreviousWndProcs = [];
    static readonly WndProcDelegate FrameProc = OnFrameMessage;

    public static void SuppressFrame(IntPtr hwnd)
    {
        var changed = false;
        if (!PreviousWndProcs.ContainsKey(hwnd))
        {
            PreviousWndProcs[hwnd] = GetWindowLong(hwnd, GwlWndProc);
            SetWindowLong(hwnd, GwlWndProc, Marshal.GetFunctionPointerForDelegate(FrameProc));
            changed = true;
        }

        var style = GetWindowLong(hwnd, GwlStyle);
        var stripped = style & ~(WsCaption | WsThickFrame | WsBorder | WsDlgFrame);
        if (stripped != style)
        {
            SetWindowLong(hwnd, GwlStyle, stripped);
            changed = true;
        }

        var ex = GetWindowLong(hwnd, GwlExStyle);
        var exStripped = (ex & ~(WsExWindowEdge | WsExClientEdge | WsExStaticEdge)) | WsExToolWindow;
        if (exStripped != ex)
        {
            SetWindowLong(hwnd, GwlExStyle, exStripped);
            changed = true;
        }

        if (changed || Borderless.Add(hwnd))
        {
            var borderless = DwmwColorNone;
            DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderless, sizeof(int));
        }

        if (changed)
        {
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }
    }

    static readonly HashSet<IntPtr> Borderless = [];

    public static bool IsAbove(IntPtr hwnd, IntPtr other)
    {
        var below = GetWindow(hwnd, GwHwndNext);
        while (below != IntPtr.Zero)
        {
            if (below == other)
            {
                return true;
            }

            below = GetWindow(below, GwHwndNext);
        }

        return false;
    }

    public static void InsertAbove(IntPtr hwnd, IntPtr sibling)
    {
        var previous = GetWindow(sibling, GwHwndPrev);
        if (previous == hwnd)
        {
            return;
        }

        SetWindowPos(hwnd, previous, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    public static bool NeedsDesktopPin(IntPtr hwnd, Func<IntPtr, bool> isOurs)
    {
        var below = GetWindow(hwnd, GwHwndNext);
        while (below != IntPtr.Zero)
        {
            if (isOurs(below) || !IsWindowVisible(below) || IsCloaked(below))
            {
                below = GetWindow(below, GwHwndNext);
                continue;
            }

            var ex = GetWindowLong(below, GwlExStyle);
            if ((ex & WsExToolWindow) != 0)
            {
                below = GetWindow(below, GwHwndNext);
                continue;
            }

            var className = ClassName(below);
            if (className is "Progman" or "WorkerW")
            {
                return false;
            }

            if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                below = GetWindow(below, GwHwndNext);
                continue;
            }

            var style = GetWindowLong(below, GwlStyle);
            if ((style & WsCaption) != 0 || (ex & WsExAppWindow) != 0 || className is "ApplicationFrameWindow")
            {
                return true;
            }

            below = GetWindow(below, GwHwndNext);
        }

        return false;
    }

    static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    static string ClassName(IntPtr hwnd)
    {
        var name = new StringBuilder(256);
        return GetClassName(hwnd, name, name.Capacity) > 0 ? name.ToString() : "";
    }

    public static bool SuspendZOrder { get; set; }

    public static Func<IntPtr, uint, nint, nint, nint?>? FilterMessage { get; set; }

    public static void BringToFront(IntPtr hwnd) => SetForegroundWindow(hwnd);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);

    const uint WmWindowPosChanging = 0x0046;

    static nint OnFrameMessage(IntPtr hwnd, uint msg, nint wParam, nint lParam)
    {
        if (FilterMessage is not null)
        {
            var filtered = FilterMessage(hwnd, msg, wParam, lParam);
            if (filtered is nint result)
            {
                return result;
            }
        }

        if (msg == WmWindowPosChanging)
        {
            PinBelowSettings(hwnd, lParam);
        }

        if (msg == WmNcCalcSize || msg == 0x0085)
        {
            return 0;
        }

        if (msg == 0x0084)
        {
            return 1;
        }

        if (!PreviousWndProcs.TryGetValue(hwnd, out var previous))
        {
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        return CallWindowProc(previous, hwnd, msg, wParam, lParam);
    }

    static void PinBelowSettings(IntPtr hwnd, nint lParam)
    {
        if (SuspendZOrder || !PanelManager.IsPanelWindow(hwnd))
        {
            return;
        }

        var lowest = PanelManager.LowestSettingsWindow();
        if (lowest == IntPtr.Zero)
        {
            return;
        }

        var pos = Marshal.PtrToStructure<WindowPos>(lParam);
        if ((pos.flags & SwpNoZOrder) != 0 || StaysBelowSettings(pos.hwndInsertAfter, lowest))
        {
            return;
        }

        pos.hwndInsertAfter = lowest;
        Marshal.StructureToPtr(pos, lParam, false);
    }

    static bool StaysBelowSettings(IntPtr insertAfter, IntPtr lowestSettings)
    {
        if (insertAfter == HwndBottom || insertAfter == lowestSettings)
        {
            return true;
        }

        if (insertAfter == IntPtr.Zero || insertAfter == new IntPtr(-1) || insertAfter == new IntPtr(-2))
        {
            return false;
        }

        return !IsAbove(insertAfter, lowestSettings);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WindowPos
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    delegate nint WndProcDelegate(IntPtr hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    static extern nint CallWindowProc(nint previous, IntPtr hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    static extern nint DefWindowProc(IntPtr hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct PointNative
    {
        public int X;
        public int Y;
    }

    public struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MonitorInfo
    {
        public int cbSize;
        public RectNative rcMonitor;
        public RectNative rcWork;
        public int dwFlags;
    }
}
