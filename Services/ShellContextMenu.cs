using System.Runtime.InteropServices;

namespace DesktopPanels.Services;

public static class ShellContextMenu
{
    const uint MfString = 0x0000;
    const uint MfSeparator = 0x0800;
    const uint CmfExplore = 0x00000004;
    const uint CmfCanRename = 0x00000010;
    const uint CmfItemMenu = 0x00000080;
    const uint CmfExtendedVerbs = 0x00000100;
    const uint CmfSyncCascade = 0x00001000;
    const uint TpmReturnCmd = 0x0100;
    const uint TpmRightButton = 0x0002;
    const uint CmdRemove = 1;
    const uint CmdFirst = 0x100;
    const uint CmdLast = 0x7FFF;
    const uint GcsVerbW = 0x00000004;
    const uint WmDrawItem = 0x002B;
    const uint WmMeasureItem = 0x002C;
    const uint WmInitMenuPopup = 0x0117;
    const uint FoDelete = 3;
    const ushort FofAllowUndo = 0x0040;

    static readonly Guid IidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    static readonly Guid IidContextMenu = new("000214e4-0000-0000-c000-000000000046");
    static readonly Guid IidContextMenu2 = new("000214f4-0000-0000-c000-000000000046");
    static readonly Guid BhidSfuiObject = new("3981e225-f559-11d3-8e3a-00c04f6837d5");

    static IContextMenu2? _activeMenu;
    static IntPtr _activeOwner;
    static bool _open;

    public static bool TryShow(IntPtr owner, string parsingName, bool removeFromPanel, string panelId, out bool rename)
    {
        rename = false;
        if (_open || string.IsNullOrWhiteSpace(parsingName))
        {
            return false;
        }

        if (owner == IntPtr.Zero)
        {
            owner = OwnerFromCursor();
        }

        if (owner == IntPtr.Zero || !TryCreate(parsingName, removeFromPanel, out var session))
        {
            return false;
        }

        _open = true;
        var previousFilter = NativeWindow.FilterMessage;
        var previousSuspend = NativeWindow.SuspendZOrder;
        var shown = false;
        try
        {
            _activeMenu = session.Menu;
            _activeOwner = owner;
            NativeWindow.FilterMessage = FilterMenuMessage;
            NativeWindow.SuspendZOrder = true;
            NativeWindow.BringToFront(owner);
            if (!NativeWindow.GetCursorPos(out var point))
            {
                return false;
            }

            var selected = TrackPopupMenuEx(session.Popup, TpmReturnCmd | TpmRightButton, point.X, point.Y, owner, IntPtr.Zero);
            shown = true;
            PostMessage(owner, 0, IntPtr.Zero, IntPtr.Zero);
            _activeMenu = null;
            NativeWindow.FilterMessage = previousFilter;
            NativeWindow.SuspendZOrder = previousSuspend;

            if (selected == CmdRemove)
            {
                PanelManager.ReturnToDefault(panelId, parsingName);
                return true;
            }

            if (selected < CmdFirst || session.Menu is null)
            {
                return true;
            }

            var offset = (uint)selected - CmdFirst;
            if (Verb(session.Menu, offset).Equals("rename", StringComparison.OrdinalIgnoreCase))
            {
                rename = true;
                return true;
            }

            InvokeOffset(session.Menu, owner, offset);
            PanelManager.RefreshAfterItemChange();
            return true;
        }
        catch
        {
            return shown;
        }
        finally
        {
            _activeMenu = null;
            _activeOwner = IntPtr.Zero;
            _open = false;
            NativeWindow.FilterMessage = previousFilter;
            NativeWindow.SuspendZOrder = previousSuspend;
            session.Dispose();
        }
    }

    public static void ShowProperties(string parsingName)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = parsingName,
                Verb = "properties",
                UseShellExecute = true
            });
        }
        catch
        {
            // Items without a property sheet leave the existing menu in place.
        }
    }

    public static void Recycle(IntPtr owner, string parsingName)
    {
        if (!File.Exists(parsingName) && !Directory.Exists(parsingName))
        {
            return;
        }

        var from = Marshal.StringToHGlobalUni(parsingName + "\0");
        try
        {
            var op = new ShFileOp
            {
                hwnd = owner,
                wFunc = FoDelete,
                pFrom = from,
                fFlags = FofAllowUndo
            };
            SHFileOperation(ref op);
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    public static bool CanRename(string parsingName) =>
        !parsingName.StartsWith("::", StringComparison.Ordinal)
        && (File.Exists(parsingName) || Directory.Exists(parsingName));

    static bool TryCreate(string parsingName, bool removeFromPanel, out Session session)
    {
        session = new Session();
        var iidItem = IidShellItem;
        var hr = SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iidItem, out var itemPtr);
        if (hr < 0 || itemPtr == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            session.Item = (IShellItem)Marshal.GetUniqueObjectForIUnknown(itemPtr);
        }
        finally
        {
            Marshal.Release(itemPtr);
        }

        var bhid = BhidSfuiObject;
        var iidMenu = IidContextMenu;
        hr = session.Item.BindToHandler(IntPtr.Zero, ref bhid, ref iidMenu, out var menuPtr);
        if (hr < 0 || menuPtr == IntPtr.Zero)
        {
            session.Dispose();
            return false;
        }

        var iidMenu2 = IidContextMenu2;
        var upgraded = Marshal.QueryInterface(menuPtr, in iidMenu2, out var menu2Ptr);
        Marshal.Release(menuPtr);
        if (upgraded < 0 || menu2Ptr == IntPtr.Zero)
        {
            session.Dispose();
            return false;
        }

        try
        {
            session.Menu = (IContextMenu2)Marshal.GetUniqueObjectForIUnknown(menu2Ptr);
        }
        finally
        {
            Marshal.Release(menu2Ptr);
        }

        session.Popup = CreatePopupMenu();
        var index = 0u;
        if (removeFromPanel)
        {
            AppendMenu(session.Popup, MfString, CmdRemove, "Remove from this panel");
            AppendMenu(session.Popup, MfSeparator, 0, null);
            index = 2;
        }

        var flags = CmfExplore | CmfCanRename | CmfItemMenu | CmfSyncCascade;
        if ((GetKeyState(0x10) & 0x8000) != 0)
        {
            flags |= CmfExtendedVerbs;
        }

        hr = session.Menu.QueryContextMenu(session.Popup, index, CmdFirst, CmdLast, flags);
        if (hr < 0)
        {
            session.Dispose();
            return false;
        }

        return true;
    }

    static string Verb(IContextMenu2 menu, uint offset)
    {
        var buffer = Marshal.AllocHGlobal(520);
        try
        {
            Marshal.WriteInt16(buffer, 0);
            var hr = menu.GetCommandString((nint)offset, GcsVerbW, IntPtr.Zero, buffer, 260);
            if (hr < 0)
            {
                return "";
            }

            return Marshal.PtrToStringUni(buffer) ?? "";
        }
        catch
        {
            return "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    static void InvokeOffset(IContextMenu2 menu, IntPtr owner, uint offset)
    {
        var info = new CmInvokeCommandInfo
        {
            cbSize = Marshal.SizeOf<CmInvokeCommandInfo>(),
            hwnd = owner,
            lpVerb = (IntPtr)offset,
            nShow = 1
        };
        menu.InvokeCommand(ref info);
    }

    static nint? FilterMenuMessage(IntPtr hwnd, uint msg, nint wParam, nint lParam)
    {
        if (_activeMenu is null || hwnd != _activeOwner)
        {
            return null;
        }

        if (msg is not (WmDrawItem or WmMeasureItem or WmInitMenuPopup))
        {
            return null;
        }

        try
        {
            if (_activeMenu.HandleMenuMsg(msg, wParam, lParam) < 0)
            {
                return null;
            }
        }
        catch
        {
            return null;
        }

        return msg == WmInitMenuPopup ? 0 : 1;
    }

    static IntPtr OwnerFromCursor()
    {
        if (!NativeWindow.GetCursorPos(out var point))
        {
            return IntPtr.Zero;
        }

        var hwnd = WindowFromPoint(point);
        if (hwnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var root = GetAncestor(hwnd, 2);
        return root == IntPtr.Zero ? hwnd : root;
    }

    sealed class Session : IDisposable
    {
        public IShellItem? Item;
        public IContextMenu2? Menu;
        public IntPtr Popup;

        public void Dispose()
        {
            if (Popup != IntPtr.Zero)
            {
                DestroyMenu(Popup);
                Popup = IntPtr.Zero;
            }

            if (Menu is not null)
            {
                Marshal.ReleaseComObject(Menu);
                Menu = null;
            }

            if (Item is not null)
            {
                Marshal.ReleaseComObject(Item);
                Item = null;
            }
        }
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        [PreserveSig]
        int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    }

    [ComImport]
    [Guid("000214f4-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IContextMenu2
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CmInvokeCommandInfo info);

        [PreserveSig]
        int GetCommandString(nint idCmd, uint uType, IntPtr reserved, IntPtr name, uint cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, nint wParam, nint lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CmInvokeCommandInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ShFileOp
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, out IntPtr ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperation(ref ShFileOp op);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool AppendMenu(IntPtr hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    static extern int TrackPopupMenuEx(IntPtr hmenu, uint flags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    static extern IntPtr WindowFromPoint(NativeWindow.PointNative point);

    [DllImport("user32.dll")]
    static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
}
