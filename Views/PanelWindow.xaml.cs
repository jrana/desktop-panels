using DesktopPanels.Models;
using DesktopPanels.Services;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using WinRT;

namespace DesktopPanels.Views;

public sealed partial class PanelWindow : Window
{
    const int Edge = 14;
    readonly PanelModel _model;
    readonly AppWindow _appWindow;
    readonly IntPtr _hwnd;
    DesktopAcrylicController? _acrylic;
    SystemBackdropConfiguration? _backdropConfig;
    IconGridView? _icons;
    SlideshowView? _slideshow;
    ClockView? _clock;
    WeatherView? _weather;
    SettingsPanel? _settings;
    bool _allowClose;
    bool _tracking;
    bool _armed;
    bool _resizing;
    EdgeHit _edge;
    int _startX;
    int _startY;
    int _originX;
    int _originY;
    int _originW;
    int _originH;
    PanelSnap.Edge _snapEdges;
    string? _iconSignature;
    string? _folderSignature;

    public PanelWindow(PanelModel model)
    {
        _model = model;
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
        _appWindow.MoveAndResize(new RectInt32(model.X, model.Y, Math.Max(PanelSnap.MinWidth, model.Width), Math.Max(PanelSnap.MinHeight, model.Height)));
        Activated += OnActivated;
        AppState.EditingChanged += OnEditingChanged;
        AppState.IconsChanged += OnIconsChanged;
        HoverClaimed += OnHoverClaimed;
        Closed += (_, _) =>
        {
            AppState.EditingChanged -= OnEditingChanged;
            AppState.IconsChanged -= OnIconsChanged;
            HoverClaimed -= OnHoverClaimed;
            _hoverWatch?.Stop();
            _acrylic?.Dispose();
            if (_settings is not null)
            {
                _settings.AllowClose();
                _settings.Close();
                _settings = null;
            }
        };
        BuildBody();
        ApplyChrome();
    }

    public bool IsInteracting => _tracking;

    public IntPtr Handle => _hwnd;

    public RectInt32 OuterBounds => new(_appWindow.Position.X, _appWindow.Position.Y, _appWindow.Size.Width, _appWindow.Size.Height);

    public IntPtr SettingsHandle => _settings?.Handle ?? IntPtr.Zero;

    public void AllowClose() => _allowClose = true;

    public void Sink()
    {
        if (!NativeWindow.NeedsDesktopPin(_hwnd, PanelManager.IsPanelWindow))
        {
            return;
        }

        NativeWindow.SetWindowPos(_hwnd, NativeWindow.HwndBottom, 0, 0, 0, 0,
            NativeWindow.SwpNoMove | NativeWindow.SwpNoSize | NativeWindow.SwpNoActivate);
        NativeWindow.SuppressFrame(_hwnd);
    }

    void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        PanelManager.KeepSettingsAbovePanels();
        NativeWindow.SuppressFrame(_hwnd);
        if (_acrylic is not null || args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        TryAcrylic();
        ApplyChrome();
        NativeWindow.SuppressFrame(_hwnd);
    }

    void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_allowClose)
        {
            args.Cancel = true;
        }
    }

    void OnEditingChanged()
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyChrome();
        }
        else
        {
            DispatcherQueue.TryEnqueue(ApplyChrome);
        }
    }

    void OnIconsChanged()
    {
        if (_model.Kind != PanelKind.Icons)
        {
            return;
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            RefreshIcons();
        }
        else
        {
            DispatcherQueue.TryEnqueue(RefreshIcons);
        }
    }

    void BuildBody()
    {
        BodyHost.Children.Clear();
        switch (_model.Kind)
        {
            case PanelKind.Icons:
                _icons = new IconGridView
                {
                    PanelId = _model.Id,
                    OwnerHwnd = _hwnd,
                    AllowDrag = true,
                    AllowRemove = !_model.IsDefault,
                    EmptyMessage = "No shortcuts in this panel"
                };
                BodyHost.Children.Add(_icons);
                RefreshIcons();
                break;
            case PanelKind.Folder:
                _icons = new IconGridView
                {
                    PanelId = _model.Id,
                    OwnerHwnd = _hwnd,
                    AllowDrag = false,
                    AllowRemove = false,
                    EmptyMessage = "Choose a folder in panel settings"
                };
                BodyHost.Children.Add(_icons);
                RefreshFolder();
                break;
            case PanelKind.Slideshow:
                _slideshow = new SlideshowView();
                BodyHost.Children.Add(_slideshow);
                _slideshow.Load(_model);
                break;
            case PanelKind.Clock:
                _clock = new ClockView();
                BodyHost.Children.Add(_clock);
                _clock.Load(_model);
                break;
            case PanelKind.Weather:
                _weather = new WeatherView();
                BodyHost.Children.Add(_weather);
                _weather.Load(_model);
                break;
        }
    }

    public void ReloadContent()
    {
        ApplyChrome();
        switch (_model.Kind)
        {
            case PanelKind.Icons:
                if (_icons is not null)
                {
                    _icons.AllowRemove = !_model.IsDefault;
                }

                RefreshIcons();
                break;
            case PanelKind.Folder:
                RefreshFolder();
                break;
            case PanelKind.Slideshow:
                _slideshow?.Load(_model);
                break;
            case PanelKind.Clock:
                _clock?.Load(_model);
                break;
            case PanelKind.Weather:
                _weather?.Load(_model);
                break;
        }
    }

    void RefreshIcons()
    {
        if (_icons is null)
        {
            return;
        }

        var signature = string.Join("|", _model.IconIds);
        var icons = _model.IconIds
            .Select(id => PanelManager.DesktopIcons.FirstOrDefault(icon => icon.ParsingName.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .Where(icon => icon is not null)
            .Cast<DesktopIcon>()
            .ToList();
        var names = signature + ":" + string.Join("|", icons.Select(icon => icon.ParsingName));
        if (names == _iconSignature)
        {
            return;
        }

        _iconSignature = names;
        _icons.SetItems(icons);
    }

    public void RefreshLiveContent()
    {
        if (_model.Kind == PanelKind.Folder)
        {
            RefreshFolder();
        }
    }

    void RefreshFolder()
    {
        if (_icons is null)
        {
            return;
        }

        _icons.EmptyMessage = string.IsNullOrWhiteSpace(_model.FolderPath)
            ? "Choose a folder in panel settings"
            : "This folder is empty";
        var items = ShellDesktop.EnumerateFolder(_model.FolderPath);
        var signature = (_model.FolderPath ?? "") + "\n" + string.Join("|", items.Select(icon => icon.ParsingName));
        if (signature == _folderSignature)
        {
            return;
        }

        _folderSignature = signature;
        _icons.SetItems(items);
    }

    void ApplyChrome()
    {
        Header.Visibility = _model.ShowTitle ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Visibility = _model.ShowTitle ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = string.IsNullOrWhiteSpace(_model.Title) ? DefaultTitle() : _model.Title;
        _icons?.SetTopInset();
        UpdateHoverActions();
        Chrome.BorderThickness = new Thickness(0);
        var tint = ParseColor(_model.TintColor);
        if (_acrylic is not null)
        {
            _acrylic.TintColor = tint;
            _acrylic.TintOpacity = (float)Math.Clamp(_model.Opacity, 0, 0.9);
            _acrylic.LuminosityOpacity = (float)Math.Clamp(_model.Opacity * 0.75, 0, 0.85);
            _acrylic.FallbackColor = tint;
            Chrome.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        else
        {
            var alpha = (byte)(Math.Clamp(_model.Opacity, 0, 0.9) * 210);
            Chrome.Background = new SolidColorBrush(Color.FromArgb(alpha, tint.R, tint.G, tint.B));
        }
    }

    void TryAcrylic()
    {
        if (!DesktopAcrylicController.IsSupported())
        {
            return;
        }

        _backdropConfig = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = SystemBackdropTheme.Dark
        };
        var tint = ParseColor(_model.TintColor);
        _acrylic = new DesktopAcrylicController
        {
            TintColor = tint,
            TintOpacity = (float)Math.Clamp(_model.Opacity, 0, 0.9),
            LuminosityOpacity = (float)Math.Clamp(_model.Opacity * 0.75, 0, 0.85),
            FallbackColor = tint
        };
        _acrylic.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
        _acrylic.SetSystemBackdropConfiguration(_backdropConfig);
        Chrome.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    static event Action<IntPtr>? HoverClaimed;

    bool _actionsShown;
    bool _addMenuOpen;
    DispatcherQueueTimer? _hoverWatch;

    void OnRootEntered(object sender, PointerRoutedEventArgs e) => ShowActions();

    void OnRootExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_addMenuOpen && !NativeWindow.CursorInside(_hwnd))
        {
            HideActions();
        }
    }

    void OnHoverClaimed(IntPtr hwnd)
    {
        if (hwnd != _hwnd)
        {
            HideActions();
        }
    }

    void ShowActions()
    {
        HoverClaimed?.Invoke(_hwnd);
        RemoveButton.Visibility = PanelManager.CanRemove(_model) ? Visibility.Visible : Visibility.Collapsed;
        FadeActions(true);
        if (_hoverWatch is null)
        {
            _hoverWatch = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _hoverWatch.Interval = TimeSpan.FromMilliseconds(50);
            _hoverWatch.IsRepeating = true;
            _hoverWatch.Tick += OnHoverWatch;
        }

        _hoverWatch.Start();
    }

    void OnHoverWatch(DispatcherQueueTimer sender, object args)
    {
        if (!_addMenuOpen && !NativeWindow.CursorInside(_hwnd))
        {
            HideActions();
        }
    }

    void HideActions()
    {
        _hoverWatch?.Stop();
        FadeActions(false);
    }

    void FadeActions(bool show)
    {
        if (_actionsShown == show && (!show || HoverActions.Opacity > 0.9))
        {
            return;
        }

        _actionsShown = show;
        var visual = ElementCompositionPreview.GetElementVisual(HoverActions);
        if (show)
        {
            HoverActions.Visibility = Visibility.Visible;
            HoverActions.IsHitTestVisible = true;
        }
        else
        {
            HoverActions.IsHitTestVisible = false;
            ResetAction(AddButton, AddIcon);
            ResetAction(SettingsButton, SettingsIcon);
            ResetAction(RemoveButton, RemoveIcon);
        }

        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        var ease = visual.Compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.2f, 0.8f), new System.Numerics.Vector2(0.2f, 1f));
        fade.InsertKeyFrame(1f, show ? 1f : 0f, ease);
        fade.Duration = TimeSpan.FromMilliseconds(show ? 180 : 260);
        visual.StartAnimation("Opacity", fade);
        if (!show)
        {
            var hide = DispatcherQueue.GetForCurrentThread().CreateTimer();
            hide.Interval = TimeSpan.FromMilliseconds(250);
            hide.Tick += (_, _) =>
            {
                if (!_actionsShown)
                {
                    HoverActions.Visibility = Visibility.Collapsed;
                }
            };
            hide.Start();
        }
    }

    void UpdateHoverActions()
    {
        if (_actionsShown)
        {
            RemoveButton.Visibility = PanelManager.CanRemove(_model) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    void OnActionEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button && button.Content is FontIcon icon)
        {
            button.Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255));
            icon.Foreground = new SolidColorBrush(Color.FromArgb(255, 22, 22, 22));
        }
    }

    void OnActionExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button && button.Content is FontIcon icon)
        {
            ResetAction(button, icon);
        }
    }

    static void ResetAction(Button button, FontIcon icon)
    {
        button.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        icon.Foreground = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255));
    }

    void OnAddMenuOpening(object sender, object e) => _addMenuOpen = true;

    void OnAddMenuClosed(object sender, object e)
    {
        _addMenuOpen = false;
        if (!NativeWindow.CursorInside(_hwnd))
        {
            HideActions();
        }
    }

    void OnAddPanel(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string name && Enum.TryParse<PanelKind>(name, out var kind))
        {
            PanelManager.AddPanel(kind);
        }
    }

    void OnSettings(object sender, RoutedEventArgs e)
    {
        if (_settings is not null)
        {
            CloseSettings();
            return;
        }

        _settings = new SettingsPanel(_model, _hwnd, PreviewSettings, CloseSettings);
        _settings.Closed += (_, _) => _settings = null;
    }

    void PreviewSettings(bool refreshContent)
    {
        ApplyChrome();
        _settings?.SyncChrome(_model);
        if (refreshContent)
        {
            ReloadContent();
        }

        _settings?.Place(_hwnd);
    }

    void CloseSettings()
    {
        if (_settings is null)
        {
            return;
        }

        var settings = _settings;
        _settings = null;
        settings.AllowClose();
        settings.Close();
        ApplyChrome();
        ReloadContent();
    }

    void OnRemove(object sender, RoutedEventArgs e) => PanelManager.Remove(_model);

    void OnRootPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (IsInside(e.OriginalSource as DependencyObject, HoverActions))
        {
            return;
        }

        var point = e.GetCurrentPoint(Root).Position;
        _edge = HitTest(point);
        _resizing = _edge != EdgeHit.None;
        if (!_resizing && IsInteractiveContent(e.OriginalSource as DependencyObject))
        {
            return;
        }

        _tracking = true;
        _armed = false;
        NativeWindow.GetCursorPos(out var cursor);
        _startX = cursor.X;
        _startY = cursor.Y;
        _originX = _appWindow.Position.X;
        _originY = _appWindow.Position.Y;
        _originW = _appWindow.Size.Width;
        _originH = _appWindow.Size.Height;
        Root.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    void OnRootMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_tracking)
        {
            return;
        }

        NativeWindow.GetCursorPos(out var cursor);
        var dx = cursor.X - _startX;
        var dy = cursor.Y - _startY;
        if (!_armed)
        {
            if (Math.Abs(dx) < 6 && Math.Abs(dy) < 6)
            {
                return;
            }

            _armed = true;
        }

        var dpi = NativeWindow.GetDpiForWindow(_hwnd);
        var gap = PanelSnap.ToPixels(PanelSnap.GapDip, dpi);
        var threshold = PanelSnap.ToPixels(PanelSnap.ThresholdDip, dpi);
        var others = PanelManager.OtherBounds(_model.Id);
        if (_resizing)
        {
            var x = _originX;
            var y = _originY;
            var w = _originW;
            var h = _originH;
            if (_edge is EdgeHit.Left or EdgeHit.TopLeft or EdgeHit.BottomLeft)
            {
                x = _originX + dx;
                w = _originW - dx;
            }

            if (_edge is EdgeHit.Right or EdgeHit.TopRight or EdgeHit.BottomRight)
            {
                w = _originW + dx;
            }

            if (_edge is EdgeHit.Top or EdgeHit.TopLeft or EdgeHit.TopRight)
            {
                y = _originY + dy;
                h = _originH - dy;
            }

            if (_edge is EdgeHit.Bottom or EdgeHit.BottomLeft or EdgeHit.BottomRight)
            {
                h = _originH + dy;
            }

            if (w < PanelSnap.MinWidth)
            {
                if (_edge is EdgeHit.Left or EdgeHit.TopLeft or EdgeHit.BottomLeft)
                {
                    x = _originX + _originW - PanelSnap.MinWidth;
                }

                w = PanelSnap.MinWidth;
            }

            if (h < PanelSnap.MinHeight)
            {
                if (_edge is EdgeHit.Top or EdgeHit.TopLeft or EdgeHit.TopRight)
                {
                    y = _originY + _originH - PanelSnap.MinHeight;
                }

                h = PanelSnap.MinHeight;
            }
            var snapped = PanelSnap.Resize(new RectInt32(x, y, w, h), MovingEdges(_edge), others, threshold);
            _appWindow.MoveAndResize(snapped.Bounds);
            ShowSnapCue(snapped.Locked);
        }
        else
        {
            var snapped = PanelSnap.Move(new RectInt32(_originX + dx, _originY + dy, _originW, _originH), others, gap, threshold);
            _appWindow.Move(new PointInt32(snapped.Bounds.X, snapped.Bounds.Y));
            ShowSnapCue(snapped.Locked);
        }

        _settings?.Place(_hwnd);
        PanelManager.KeepSettingsAbovePanels();

        e.Handled = true;
    }

    void OnRootReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_tracking)
        {
            return;
        }

        _tracking = false;
        ShowSnapCue(PanelSnap.Edge.None);
        Root.ReleasePointerCapture(e.Pointer);
        if (_armed)
        {
            PanelManager.RememberBounds(_model, _appWindow.Position.X, _appWindow.Position.Y, _appWindow.Size.Width, _appWindow.Size.Height);
        }
    }

    bool IsInteractiveContent(DependencyObject? source)
    {
        while (source is not null && source != Root)
        {
            if (source == HoverActions || source is Button || source is FontIcon || source is Image)
            {
                return true;
            }

            if (source is TextBlock && IsInside(source, BodyHost))
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    static bool IsInside(DependencyObject? source, DependencyObject ancestor)
    {
        while (source is not null)
        {
            if (source == ancestor)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    void ShowSnapCue(PanelSnap.Edge edges)
    {
        if (edges == _snapEdges)
        {
            return;
        }

        _snapEdges = edges;
        SnapCue.BorderThickness = new Thickness(
            (edges & PanelSnap.Edge.Left) != 0 ? 2 : 0,
            (edges & PanelSnap.Edge.Top) != 0 ? 2 : 0,
            (edges & PanelSnap.Edge.Right) != 0 ? 2 : 0,
            (edges & PanelSnap.Edge.Bottom) != 0 ? 2 : 0);
    }

    static PanelSnap.Edge MovingEdges(EdgeHit edge) => edge switch
    {
        EdgeHit.Left => PanelSnap.Edge.Left,
        EdgeHit.Right => PanelSnap.Edge.Right,
        EdgeHit.Top => PanelSnap.Edge.Top,
        EdgeHit.Bottom => PanelSnap.Edge.Bottom,
        EdgeHit.TopLeft => PanelSnap.Edge.Top | PanelSnap.Edge.Left,
        EdgeHit.TopRight => PanelSnap.Edge.Top | PanelSnap.Edge.Right,
        EdgeHit.BottomLeft => PanelSnap.Edge.Bottom | PanelSnap.Edge.Left,
        EdgeHit.BottomRight => PanelSnap.Edge.Bottom | PanelSnap.Edge.Right,
        _ => PanelSnap.Edge.None
    };

    EdgeHit HitTest(Windows.Foundation.Point point)
    {
        var width = Root.ActualWidth;
        var height = Root.ActualHeight;
        var left = point.X <= Edge;
        var right = point.X >= width - Edge;
        var top = point.Y <= Edge;
        var bottom = point.Y >= height - Edge;
        if (top && left) return EdgeHit.TopLeft;
        if (top && right) return EdgeHit.TopRight;
        if (bottom && left) return EdgeHit.BottomLeft;
        if (bottom && right) return EdgeHit.BottomRight;
        if (left) return EdgeHit.Left;
        if (right) return EdgeHit.Right;
        if (top) return EdgeHit.Top;
        if (bottom) return EdgeHit.Bottom;
        return EdgeHit.None;
    }

    string DefaultTitle() => _model.Kind switch
    {
        PanelKind.Icons => "Shortcuts",
        PanelKind.Folder => "Folder",
        PanelKind.Slideshow => "Slideshow",
        PanelKind.Clock => "Clock",
        PanelKind.Weather => "Weather",
        _ => "Panel"
    };

    public static Color ParseColor(string hex)
    {
        var text = hex.Trim().TrimStart('#');
        if (text.Length != 6 || !int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            return Color.FromArgb(255, 47, 111, 237);
        }

        return Color.FromArgb(255, (byte)((rgb >> 16) & 255), (byte)((rgb >> 8) & 255), (byte)(rgb & 255));
    }

    enum EdgeHit
    {
        None,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }
}
