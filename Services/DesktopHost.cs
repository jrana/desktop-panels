using System.Runtime.InteropServices;

namespace DesktopPanels.Services;

/// <summary>
/// Hides and restores Explorer's desktop icon list. Panels stay top-level:
/// WinUI rejects parent-chain changes, and owning WorkerW would destroy the
/// panels when Explorer restarts. Z-order is pinned to HWND_BOTTOM instead.
/// </summary>
public static class DesktopHost
{
    const uint WmSpawnWorker = 0x052C;
    const int SwHide = 0;
    const int SwShow = 5;

    static IntPtr _listView;

    public static void HideIcons()
    {
        var list = FindListView();
        if (list == IntPtr.Zero)
        {
            return;
        }

        _listView = list;
        ShowWindow(list, SwHide);
        AppState.DesktopIconsHidden = true;
    }

    public static void ShowIcons()
    {
        var list = _listView != IntPtr.Zero && IsWindow(_listView) ? _listView : FindListView();
        if (list != IntPtr.Zero)
        {
            ShowWindow(list, SwShow);
        }

        _listView = IntPtr.Zero;
        AppState.DesktopIconsHidden = false;
    }

    public static void EnsureHidden()
    {
        if (!AppState.DesktopIconsHidden)
        {
            return;
        }

        if (_listView != IntPtr.Zero && IsWindow(_listView))
        {
            if (IsWindowVisible(_listView))
            {
                ShowWindow(_listView, SwHide);
            }

            return;
        }

        var list = FindListView();
        if (list == IntPtr.Zero)
        {
            return;
        }

        _listView = list;
        if (IsWindowVisible(list))
        {
            ShowWindow(list, SwHide);
        }
    }

    static IntPtr FindListView()
    {
        var progman = FindWindow("Progman", null);
        var defView = FindDefView(progman);
        if (defView == IntPtr.Zero && progman != IntPtr.Zero)
        {
            // Creating WorkerW repeatedly redraws the wallpaper, and acrylic panels flash with it.
            SendMessage(progman, WmSpawnWorker, IntPtr.Zero, IntPtr.Zero);
            defView = FindDefView(progman);
        }

        if (defView == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        return FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
    }

    static IntPtr FindDefView(IntPtr progman)
    {
        var defView = progman == IntPtr.Zero
            ? IntPtr.Zero
            : FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView != IntPtr.Zero)
        {
            return defView;
        }

        EnumWindows((hwnd, _) =>
        {
            var child = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (child != IntPtr.Zero)
            {
                defView = child;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return defView;
    }

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);
}
