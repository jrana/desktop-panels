using System.Collections.Concurrent;
using DesktopPanels.Models;
using DesktopPanels.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace DesktopPanels.Services;

public static class PanelManager
{
    static LayoutDocument _layout = new();
    static readonly Dictionary<string, PanelWindow> _windows = new(StringComparer.Ordinal);
    static readonly List<DesktopIcon> _desktopIcons = [];
    static Window? _anchor;
    static TrayIcon? _tray;
    static DispatcherQueueTimer? _maintainTimer;
    static DispatcherQueueTimer? _iconTimer;
    static DispatcherQueue? _dispatcher;
    static readonly BlockingCollection<Action> ScanQueue = new();
    static Thread? _scanThread;
    static int _scanPending;
    static bool _quitting;
    static Mutex? _mutex;

    public static IReadOnlyList<DesktopIcon> DesktopIcons => _desktopIcons;

    public static void Start()
    {
        _mutex = new Mutex(false, @"Local\DesktopPanels.SingleInstance");
        try
        {
            if (!_mutex.WaitOne(0))
            {
                Environment.Exit(0);
                return;
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous instance crashed. This process now owns the mutex.
        }

        Watchdog.Spawn();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (!_quitting)
            {
                DesktopHost.ShowIcons();
            }
        };

        _layout = LayoutStore.Load();
        SyncDesktopIcons();
        LayoutStore.Save(_layout);
        DesktopHost.HideIcons();

        _anchor = new Window();
        _anchor.Content = new Microsoft.UI.Xaml.Controls.Grid();
        _anchor.Activate();
        var anchorHwnd = WinRT.Interop.WindowNative.GetWindowHandle(_anchor);
        var anchorWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(anchorHwnd));
        AppIcon.Apply(anchorWindow);
        anchorWindow.IsShownInSwitchers = false;
        anchorWindow.MoveAndResize(new RectInt32(-32000, -32000, 10, 10));
        var ex = NativeWindow.GetWindowLong(anchorHwnd, NativeWindow.GwlExStyle);
        NativeWindow.SetWindowLong(anchorHwnd, NativeWindow.GwlExStyle, ex | NativeWindow.WsExToolWindow);

        foreach (var panel in _layout.Panels)
        {
            OpenWindow(panel);
        }

        _tray = new TrayIcon();
        _tray.Show();

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _maintainTimer = _dispatcher.CreateTimer();
        _maintainTimer.Interval = TimeSpan.FromMilliseconds(700);
        _maintainTimer.Tick += (_, _) => MaintainDesktop();
        _maintainTimer.Start();

        _iconTimer = _dispatcher.CreateTimer();
        _iconTimer.Interval = TimeSpan.FromSeconds(4);
        _iconTimer.Tick += (_, _) => RequestDesktopScan();
        _iconTimer.Start();
    }

    public static void AddPanel(PanelKind kind)
    {
        var index = _layout.Panels.Count;
        var model = new PanelModel
        {
            Kind = kind,
            X = 120 + (index % 8) * 28,
            Y = 96 + (index % 8) * 28,
            Title = kind switch
            {
                PanelKind.Icons => "Shortcuts",
                PanelKind.Folder => "Folder",
                PanelKind.Slideshow => "Slideshow",
                PanelKind.Clock => "Clock",
                PanelKind.Weather => "Weather",
                PanelKind.Date => "Date",
                _ => "Panel"
            },
            ShowTitle = true,
            TintColor = kind switch
            {
                PanelKind.Clock => "#1F8A70",
                PanelKind.Weather => "#C47B2B",
                PanelKind.Slideshow => "#A84865",
                PanelKind.Folder => "#6B7C93",
                PanelKind.Date => "#2C2C2C",
                _ => "#2F6FED"
            }
        };

        model.Width = kind switch
        {
            PanelKind.Clock => 300,
            PanelKind.Weather => 380,
            PanelKind.Slideshow => 520,
            PanelKind.Date => 360,
            _ => 460
        };
        model.Height = kind switch
        {
            PanelKind.Clock => 170,
            PanelKind.Weather => model.ShowSevenDay ? 300 : 180,
            PanelKind.Slideshow => 320,
            PanelKind.Date => 440,
            _ => 420
        };

        if (kind == PanelKind.Clock)
        {
            model.City = "Local";
        }

        _layout.Panels.Add(model);
        LayoutStore.Save(_layout);
        OpenWindow(model);
    }

    static PanelModel? BasePanel() =>
        _layout.Panels.FirstOrDefault(p => p.Kind == PanelKind.Icons && p.IsDefault)
        ?? _layout.Panels.FirstOrDefault(p => p.Kind == PanelKind.Icons);

    public static bool CanRemove(PanelModel model)
    {
        var home = BasePanel();
        if (home is not null && model.Id == home.Id)
        {
            return false;
        }

        var first = _layout.Panels.FirstOrDefault();
        return first is null || model.Id != first.Id;
    }

    public static void Remove(PanelModel model)
    {
        if (!CanRemove(model))
        {
            return;
        }

        var home = BasePanel();
        if (home is not null)
        {
            foreach (var id in model.IconIds)
            {
                if (!home.IconIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    home.IconIds.Add(id);
                }
            }
        }

        _layout.Panels.Remove(model);
        LayoutStore.EnsureDefaultIconPanel(_layout);
        if (_windows.Remove(model.Id, out var window))
        {
            window.AllowClose();
            window.Close();
        }

        LayoutStore.Save(_layout);
        AppState.NotifyIconsChanged();
    }

    public static void MoveIcon(string sourcePanelId, string targetPanelId, string parsingName)
    {
        if (sourcePanelId == targetPanelId)
        {
            return;
        }

        var source = _layout.Panels.FirstOrDefault(p => p.Id == sourcePanelId && p.Kind == PanelKind.Icons);
        var target = _layout.Panels.FirstOrDefault(p => p.Id == targetPanelId && p.Kind == PanelKind.Icons);
        if (source is null || target is null)
        {
            return;
        }

        source.IconIds.RemoveAll(id => id.Equals(parsingName, StringComparison.OrdinalIgnoreCase));
        if (!target.IconIds.Contains(parsingName, StringComparer.OrdinalIgnoreCase))
        {
            target.IconIds.Add(parsingName);
        }

        LayoutStore.Save(_layout);
        AppState.NotifyIconsChanged();
    }

    public static void ReturnToDefault(string sourcePanelId, string parsingName)
    {
        var source = _layout.Panels.FirstOrDefault(p => p.Id == sourcePanelId);
        var target = _layout.Panels.FirstOrDefault(p => p.Kind == PanelKind.Icons && p.IsDefault);
        if (source is null || target is null || source.Id == target.Id)
        {
            return;
        }

        source.IconIds.RemoveAll(id => id.Equals(parsingName, StringComparison.OrdinalIgnoreCase));
        if (!target.IconIds.Contains(parsingName, StringComparer.OrdinalIgnoreCase))
        {
            target.IconIds.Add(parsingName);
        }

        LayoutStore.Save(_layout);
        AppState.NotifyIconsChanged();
    }

    public static void Save() => LayoutStore.Save(_layout);

    public static void RememberBounds(PanelModel model, int x, int y, int width, int height)
    {
        model.X = x;
        model.Y = y;
        model.Width = width;
        model.Height = height;
        LayoutStore.Save(_layout);
    }

    public static IReadOnlyList<RectInt32> OtherBounds(string exceptId)
    {
        var bounds = new List<RectInt32>(_windows.Count);
        foreach (var (id, window) in _windows)
        {
            if (id == exceptId)
            {
                continue;
            }

            bounds.Add(window.OuterBounds);
        }

        return bounds;
    }

    public static bool IsPanelWindow(IntPtr hwnd)
    {
        foreach (var window in _windows.Values)
        {
            if (window.Handle == hwnd)
            {
                return true;
            }
        }

        return false;
    }

    public static IntPtr LowestSettingsWindow()
    {
        IntPtr lowest = IntPtr.Zero;
        foreach (var window in _windows.Values)
        {
            var hwnd = window.SettingsHandle;
            if (hwnd == IntPtr.Zero)
            {
                continue;
            }

            if (lowest == IntPtr.Zero || NativeWindow.IsAbove(lowest, hwnd))
            {
                lowest = hwnd;
            }
        }

        return lowest;
    }

    public static void KeepSettingsAbovePanels()
    {
        IntPtr topPanel = 0;
        var settings = new List<IntPtr>();
        foreach (var window in _windows.Values)
        {
            var hwnd = window.Handle;
            if (topPanel == IntPtr.Zero || NativeWindow.IsAbove(hwnd, topPanel))
            {
                topPanel = hwnd;
            }

            var settingsHwnd = window.SettingsHandle;
            if (settingsHwnd != IntPtr.Zero)
            {
                settings.Add(settingsHwnd);
            }
        }

        if (topPanel == IntPtr.Zero || settings.Count == 0)
        {
            return;
        }

        foreach (var hwnd in settings)
        {
            var covered = false;
            foreach (var window in _windows.Values)
            {
                if (NativeWindow.IsAbove(window.Handle, hwnd))
                {
                    covered = true;
                    break;
                }
            }

            if (covered)
            {
                NativeWindow.InsertAbove(hwnd, topPanel);
            }
        }
    }

    public static void Quit()
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;
        _maintainTimer?.Stop();
        _iconTimer?.Stop();
        DesktopHost.ShowIcons();
        _tray?.Dispose();
        foreach (var window in _windows.Values.ToList())
        {
            window.AllowClose();
            window.Close();
        }

        _windows.Clear();
        _anchor?.Close();
        Application.Current.Exit();
    }

    static void OpenWindow(PanelModel model)
    {
        var window = new PanelWindow(model);
        _windows[model.Id] = window;
        window.Activate();
        KeepSettingsAbovePanels();
    }

    static bool SyncDesktopIcons() => ApplyDesktop(ShellDesktop.Enumerate());

    static bool ApplyDesktop(IReadOnlyList<DesktopIcon> icons)
    {
        var renamed = PreserveRenames(icons);
        var changed = renamed || icons.Count != _desktopIcons.Count
            || icons.Where((icon, index) => !icon.ParsingName.Equals(_desktopIcons.ElementAtOrDefault(index)?.ParsingName, StringComparison.OrdinalIgnoreCase)).Any();
        _desktopIcons.Clear();
        _desktopIcons.AddRange(icons);

        var known = icons.Select(icon => icon.ParsingName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var iconPanels = _layout.Panels.Where(p => p.Kind == PanelKind.Icons).ToList();
        LayoutStore.EnsureDefaultIconPanel(_layout);
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var panel in iconPanels)
        {
            var before = panel.IconIds.Count;
            panel.IconIds = panel.IconIds
                .Where(id => known.Contains(id) && assigned.Add(id))
                .ToList();
            if (panel.IconIds.Count != before)
            {
                changed = true;
            }
        }

        var home = _layout.Panels.First(p => p.Kind == PanelKind.Icons && p.IsDefault);
        foreach (var icon in icons)
        {
            if (assigned.Add(icon.ParsingName))
            {
                home.IconIds.Add(icon.ParsingName);
                changed = true;
            }
        }

        return changed;
    }

    static bool PreserveRenames(IReadOnlyList<DesktopIcon> icons)
    {
        if (_desktopIcons.Count == 0)
        {
            return false;
        }

        var previous = _desktopIcons.Select(icon => icon.ParsingName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = icons.Select(icon => icon.ParsingName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = previous.Where(id => !current.Contains(id)).ToList();
        var added = icons.Where(icon => !previous.Contains(icon.ParsingName)).ToList();
        var changed = false;
        foreach (var oldId in removed)
        {
            var parent = Path.GetDirectoryName(oldId);
            if (string.IsNullOrEmpty(parent))
            {
                continue;
            }

            var removedHere = removed.Count(id => string.Equals(Path.GetDirectoryName(id), parent, StringComparison.OrdinalIgnoreCase));
            var matches = added.Where(icon => string.Equals(Path.GetDirectoryName(icon.ParsingName), parent, StringComparison.OrdinalIgnoreCase)).ToList();
            if (removedHere != 1 || matches.Count != 1)
            {
                continue;
            }

            foreach (var panel in _layout.Panels)
            {
                var index = panel.IconIds.FindIndex(id => id.Equals(oldId, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    continue;
                }

                panel.IconIds[index] = matches[0].ParsingName;
                changed = true;
            }

            added.Remove(matches[0]);
        }

        return changed;
    }

    public static void RefreshAfterItemChange()
    {
        try
        {
            if (SyncDesktopIcons())
            {
                LayoutStore.Save(_layout);
                AppState.NotifyIconsChanged();
            }

            foreach (var window in _windows.Values)
            {
                window.RefreshLiveContent();
            }
        }
        catch
        {
            // Explorer can be restarting. The timer retries.
        }
    }

    static void MaintainDesktop()
    {
        try
        {
            if (NativeWindow.SuspendZOrder)
            {
                return;
            }

            // Re-hide Explorer's icon list only. Z-order is handled in
            // WM_WINDOWPOSCHANGING; sinking from this timer stalls the cursor.
            DesktopHost.EnsureHidden();
        }
        catch
        {
            // Explorer can be mid-restart. The next tick tries again.
        }
    }

    static void RequestDesktopScan()
    {
        if (_quitting || _dispatcher is null || Interlocked.Exchange(ref _scanPending, 1) == 1)
        {
            return;
        }

        var folders = _layout.Panels
            .Where(panel => panel.Kind == PanelKind.Folder)
            .Select(panel => panel.FolderPath)
            .ToArray();
        EnsureScanner();
        ScanQueue.Add(() => RunDesktopScan(folders));
    }

    static void EnsureScanner()
    {
        if (_scanThread is not null)
        {
            return;
        }

        var thread = new Thread(ScanLoop)
        {
            IsBackground = true,
            Name = "DesktopPanels.Shell"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        _scanThread = thread;
    }

    static void ScanLoop()
    {
        foreach (var work in ScanQueue.GetConsumingEnumerable())
        {
            try
            {
                work();
            }
            catch
            {
                // RunDesktopScan clears the pending flag if it never posts back.
            }
        }
    }

    static void RunDesktopScan(string?[] folders)
    {
        IReadOnlyList<ShellDesktop.ListedIcon>? listed = null;
        var posted = false;
        try
        {
            try
            {
                listed = ShellDesktop.ListDesktop();
                foreach (var folder in folders)
                {
                    ShellDesktop.PrefetchFolder(folder);
                }
            }
            catch
            {
                listed = null;
            }

            var snapshot = listed;
            posted = _dispatcher is not null && _dispatcher.TryEnqueue(() => ApplyDesktopScan(snapshot));
        }
        finally
        {
            if (!posted)
            {
                Interlocked.Exchange(ref _scanPending, 0);
            }
        }
    }

    static void ApplyDesktopScan(IReadOnlyList<ShellDesktop.ListedIcon>? listed)
    {
        try
        {
            if (_quitting || listed is null)
            {
                return;
            }

            if (ApplyDesktop(ShellDesktop.Materialize(listed)))
            {
                LayoutStore.Save(_layout);
                AppState.NotifyIconsChanged();
            }

            foreach (var window in _windows.Values)
            {
                window.RefreshLiveContent();
            }
        }
        catch
        {
            // Explorer can be restarting. The timer retries.
        }
        finally
        {
            Interlocked.Exchange(ref _scanPending, 0);
        }
    }
}
