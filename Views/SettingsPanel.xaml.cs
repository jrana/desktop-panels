using DesktopPanels.Models;
using DesktopPanels.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace DesktopPanels.Views;

public sealed partial class SettingsPanel : Window
{
    const int Gap = 10;
    static readonly Color BackdropColor = Color.FromArgb(255, 0x2C, 0x3E, 0x50);
    readonly IntPtr _hwnd;
    readonly AppWindow _appWindow;
    readonly PanelSettingsWindow _form;
    bool _allowClose;

    public SettingsPanel(PanelModel model, IntPtr ownerHwnd, Action<bool> preview, Action close)
    {
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        AppIcon.Apply(_appWindow);
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        _appWindow.SetPresenter(presenter);
        _appWindow.IsShownInSwitchers = false;
        _appWindow.Closing += OnClosing;
        var style = NativeWindow.GetWindowLong(_hwnd, NativeWindow.GwlExStyle);
        NativeWindow.SetWindowLong(_hwnd, NativeWindow.GwlExStyle, style | NativeWindow.WsExToolWindow);
        var corner = NativeWindow.DwmwcpRound;
        NativeWindow.DwmSetWindowAttribute(_hwnd, NativeWindow.DwmwaWindowCornerPreference, ref corner, sizeof(int));
        NativeWindow.SuppressFrame(_hwnd);
        _form = new PanelSettingsWindow(model, _hwnd, preview, close);
        Host.Children.Add(_form);
        _form.LayoutReady += () => Place(ownerHwnd);
        Activated += OnActivated;
        SyncChrome(model);
        Place(ownerHwnd);
        Activate();
    }

    public IntPtr Handle => _hwnd;

    public void AllowClose() => _allowClose = true;

    public void SyncChrome(PanelModel model)
    {
        Chrome.Background = new SolidColorBrush(BackdropColor);
    }

    public void Place(IntPtr ownerHwnd)
    {
        if (!NativeWindow.GetWindowRect(ownerHwnd, out var owner))
        {
            return;
        }

        var work = NativeWindow.WorkArea(ownerHwnd);
        var workWidth = Math.Max(1, work.Right - work.Left);
        var measured = _form.MeasureSize();
        var scale = Math.Max(1, NativeWindow.GetDpiForWindow(ownerHwnd) / 96.0);
        var width = (int)Math.Ceiling(Math.Min(measured.Width * scale, workWidth));
        var x = owner.Left;
        if (x + width > work.Right)
        {
            x = work.Right - width;
        }

        if (x < work.Left)
        {
            x = work.Left;
        }

        var contentDip = Math.Max(measured.Height, _form.LaidOutHeight);
        var desired = (int)Math.Ceiling(contentDip * scale) + 4;
        var spaceBelow = work.Bottom - owner.Bottom - Gap;
        var spaceAbove = owner.Top - work.Top - Gap;
        int y;
        int height;
        if (spaceBelow >= desired)
        {
            y = owner.Bottom + Gap;
            height = desired;
        }
        else if (spaceAbove >= desired)
        {
            height = desired;
            y = owner.Top - Gap - height;
        }
        else if (spaceBelow >= spaceAbove)
        {
            y = owner.Bottom + Gap;
            height = Math.Max(140, spaceBelow);
        }
        else
        {
            height = Math.Max(140, spaceAbove);
            y = owner.Top - Gap - height;
        }

        _form.ShowScroll(height + 2 < desired);
        var moved = _appWindow.Position.X != x || _appWindow.Position.Y != y || _appWindow.Size.Width != width || _appWindow.Size.Height != height;
        if (moved)
        {
            _appWindow.MoveAndResize(new RectInt32(x, y, width, height));
            NativeWindow.SuppressFrame(_hwnd);
        }

        PanelManager.KeepSettingsAbovePanels();
    }

    void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_allowClose)
        {
            args.Cancel = true;
        }
    }

    void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        NativeWindow.SuppressFrame(_hwnd);
        Chrome.Background = new SolidColorBrush(BackdropColor);
    }
}
