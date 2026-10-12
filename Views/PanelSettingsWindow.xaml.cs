using System.Drawing.Text;
using DesktopPanels.Models;
using DesktopPanels.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.UI;

namespace DesktopPanels.Views;

public sealed partial class PanelSettingsWindow : UserControl
{
    static readonly (string Name, string Hex)[] Swatches =
    [
        ("Slate", "#6B7C93"),
        ("Blue", "#2F6FED"),
        ("Teal", "#1F8A70"),
        ("Amber", "#C47B2B"),
        ("Rose", "#A84865"),
        ("Graphite", "#2C2C2C")
    ];

    readonly PanelModel _model;
    readonly PanelModel _snapshot = new();
    readonly Action<bool> _preview;
    readonly Action _close;
    readonly IntPtr _ownerHwnd;
    readonly List<Button> _swatches = [];
    readonly CheckBox _showTitle = new() { Content = "Show title" };
    readonly TextBox _title = new();
    readonly Slider _opacity = new() { Minimum = 0, Maximum = 0.9, StepFrequency = 0.05 };
    readonly TextBlock _opacityLabel = new();
    string _tint;
    TextBox? _folder;
    NumberBox? _interval;
    ComboBox? _fit;
    ComboBox? _zones;
    ComboBox? _hourFormat;
    ComboBox? _clockFace;
    CheckBox? _showSeconds;
    CheckBox? _showCity;
    ComboBox? _clockFont;
    ComboBox? _dateFace;
    ComboBox? _dateFormat;
    CheckBox? _showWeekday;
    ComboBox? _dateFont;
    TextBlock? _dateFormatLabel;
    TextBox? _city;
    TextBox? _weatherQuery;
    ListBox? _places;
    IReadOnlyList<GeoPlace> _results = [];
    ComboBox? _unit;
    CheckBox? _sevenDay;
    readonly CheckBox _startWithWindows = new() { Content = "Start with Windows" };
    readonly WindowsStartup.Snapshot _startupSnapshot = WindowsStartup.Capture();
    readonly bool _startupEnabled = WindowsStartup.IsEnabled();
    bool _ignoreStartup;
    bool _startupTouched;

    public (double Width, double Height) MeasureSize()
    {
        const double capWidth = 380;
        var margin = Form.Margin;
        double contentWidth = 0;
        double contentHeight = margin.Top + margin.Bottom;
        var count = 0;
        foreach (var child in Form.Children.OfType<FrameworkElement>())
        {
            child.Measure(new Windows.Foundation.Size(capWidth, 160));
            var measuredWidth = child.DesiredSize.Width;
            var measuredHeight = child.DesiredSize.Height;
            if (double.IsNaN(measuredWidth) || double.IsInfinity(measuredWidth) || measuredWidth > capWidth)
            {
                measuredWidth = capWidth;
            }

            if (child is ComboBox || double.IsNaN(measuredHeight) || double.IsInfinity(measuredHeight) || measuredHeight > 180)
            {
                measuredHeight = child is ListBox ? 140 : 36;
            }

            contentWidth = Math.Max(contentWidth, measuredWidth);
            contentHeight += measuredHeight;
            count++;
        }

        if (count > 1)
        {
            contentHeight += Form.Spacing * (count - 1);
        }

        contentHeight += 48;
        var width = Math.Clamp(contentWidth + margin.Left + margin.Right, 260, capWidth + margin.Left + margin.Right);
        return (width, Math.Max(120, contentHeight));
    }

    public event Action? LayoutReady;

    public void ShowScroll(bool show)
    {
        Scroller.VerticalScrollBarVisibility = show ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }

    public double LaidOutHeight
    {
        get
        {
            if (Form.ActualHeight <= 1 || Actions.ActualHeight <= 1)
            {
                return 0;
            }

            return Form.ActualHeight + Actions.ActualHeight;
        }
    }

    public PanelSettingsWindow(PanelModel model, IntPtr ownerHwnd, Action<bool> preview, Action close)
    {
        _model = model;
        _ownerHwnd = ownerHwnd;
        _preview = preview;
        _close = close;
        _tint = model.TintColor;
        Capture();
        InitializeComponent();
        RequestedTheme = ElementTheme.Dark;
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
        Form.SizeChanged += (_, _) => LayoutReady?.Invoke();
        Actions.SizeChanged += (_, _) => LayoutReady?.Invoke();
        BuildForm();
    }

    void BuildForm()
    {
        _showTitle.IsChecked = _model.ShowTitle;
        _showTitle.VerticalAlignment = VerticalAlignment.Center;
        _title.Text = _model.Title;
        Form.Children.Add(FieldRow(_showTitle, _title));

        var colors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var swatch in Swatches)
        {
            var button = new Button
            {
                Width = 32,
                Height = 32,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Tag = swatch.Hex,
                Background = new SolidColorBrush(PanelWindow.ParseColor(swatch.Hex)),
                Style = (Style)Resources["FlatIconButton"]
            };
            ToolTipService.SetToolTip(button, swatch.Name);
            button.Click += (_, _) =>
            {
                _tint = swatch.Hex;
                MarkSwatches();
                ApplyLive(false);
            };
            _swatches.Add(button);
            colors.Children.Add(button);
        }

        MarkSwatches();

        Form.Children.Add(colors);
        _opacity.Value = Math.Clamp(_model.Opacity, 0, 0.9);
        _opacity.ValueChanged += (_, _) =>
        {
            _opacityLabel.Text = $"{_opacity.Value:0.00}";
            ApplyLive(false);
        };
        _opacityLabel.Text = $"{_opacity.Value:0.00}";
        _opacityLabel.VerticalAlignment = VerticalAlignment.Center;
        _opacityLabel.MinWidth = 36;
        Form.Children.Add(FieldRow(_opacity, _opacityLabel));

        switch (_model.Kind)
        {
            case PanelKind.Folder:
            case PanelKind.Slideshow:
                AddFolderPicker();
                break;
        }

        if (_model.Kind == PanelKind.Slideshow)
        {
            Form.Children.Add(Label("Seconds between images"));
            _interval = new NumberBox
            {
                Value = _model.SlideshowIntervalSeconds,
                Minimum = 2,
                Maximum = 3600,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
            };
            Form.Children.Add(_interval);
            Form.Children.Add(Label("Fit"));
            _fit = new ComboBox { MaxHeight = 36 };
            _fit.Items.Add("Cover");
            _fit.Items.Add("Contain");
            _fit.SelectedItem = _model.SlideshowFit.Equals("Contain", StringComparison.OrdinalIgnoreCase) ? "Contain" : "Cover";
            Form.Children.Add(_fit);
        }

        if (_model.Kind == PanelKind.Clock)
        {
            _clockFace = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxHeight = 36, MinWidth = 140 };
            _clockFace.Items.Add("Digital");
            _clockFace.Items.Add("Swiss");
            _clockFace.SelectedItem = _model.ClockFace.Equals("Swiss", StringComparison.OrdinalIgnoreCase) ? "Swiss" : "Digital";
            var faceLabel = Label("Face");
            faceLabel.VerticalAlignment = VerticalAlignment.Center;
            Form.Children.Add(FieldRow(faceLabel, _clockFace));
            _city = new TextBox { Text = _model.City };
            var cityLabel = Label("City name");
            cityLabel.VerticalAlignment = VerticalAlignment.Center;
            Form.Children.Add(FieldRow(cityLabel, _city));
            _zones = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxHeight = 36, MinWidth = 180 };
            _zones.Items.Add(new ZoneChoice(null, "Local time"));
            foreach (var zone in TimeZoneInfo.GetSystemTimeZones().OrderBy(zone => zone.DisplayName))
            {
                _zones.Items.Add(new ZoneChoice(zone.Id, zone.DisplayName));
            }

            _zones.SelectedItem = _zones.Items.OfType<ZoneChoice>().FirstOrDefault(choice => choice.Id == _model.TimeZoneId)
                ?? _zones.Items[0];
            var zoneLabel = Label("Time zone");
            zoneLabel.VerticalAlignment = VerticalAlignment.Center;
            Form.Children.Add(FieldRow(zoneLabel, _zones));
            _hourFormat = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxHeight = 36, MinWidth = 120 };
            _hourFormat.Items.Add("12 hour");
            _hourFormat.Items.Add("24 hour");
            _hourFormat.SelectedIndex = _model.Use24Hour ? 1 : 0;
            var hourLabel = Label("Hour format");
            hourLabel.VerticalAlignment = VerticalAlignment.Center;
            Form.Children.Add(FieldRow(hourLabel, _hourFormat));
            _showSeconds = new CheckBox { Content = "Show seconds", IsChecked = _model.ShowSeconds, VerticalAlignment = VerticalAlignment.Center };
            Form.Children.Add(_showSeconds);
            _showCity = new CheckBox { Content = "Show city name", IsChecked = _model.ShowCity, VerticalAlignment = VerticalAlignment.Center };
            Form.Children.Add(_showCity);
            _clockFont = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxHeight = 36, MinWidth = 160, MaxDropDownHeight = 280 };
            foreach (var family in new InstalledFontCollection().Families.Select(font => font.Name).Distinct().OrderBy(name => name))
            {
                _clockFont.Items.Add(family);
            }

            if (!_clockFont.Items.OfType<string>().Any(name => name.Equals(_model.ClockFontFamily, StringComparison.OrdinalIgnoreCase)))
            {
                _clockFont.Items.Insert(0, _model.ClockFontFamily);
            }

            _clockFont.SelectedItem = _clockFont.Items.OfType<string>().First(name => name.Equals(_model.ClockFontFamily, StringComparison.OrdinalIgnoreCase));
            var fontLabel = Label("Font");
            fontLabel.VerticalAlignment = VerticalAlignment.Center;
            Form.Children.Add(FieldRow(fontLabel, _clockFont));
        }

        if (_model.Kind == PanelKind.Date)
        {
            _dateFace = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxHeight = 36, MinWidth = 140 };
            _dateFace.Items.Add("Date");
            _dateFace.Items.Add("Calendar");
            _dateFace.SelectedItem = _model.DateFace.Equals("Calendar", StringComparison.OrdinalIgnoreCase) ? "Calendar" : "Date";
            var displayLabel = Label("Display");
            displayLabel.VerticalAlignment = VerticalAlignment.Center;
            Form.Children.Add(FieldRow(displayLabel, _dateFace));
            _dateFormat = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxHeight = 36, MinWidth = 160, MaxDropDownHeight = 280 };
            foreach (var choice in DateFormats)
            {
                _dateFormat.Items.Add(choice);
            }

            var formatMatch = _dateFormat.Items.OfType<DateFormatChoice>().FirstOrDefault(choice => choice.Pattern.Equals(_model.DateFormat, StringComparison.Ordinal));
            if (formatMatch is null)
            {
                formatMatch = new DateFormatChoice(string.IsNullOrWhiteSpace(_model.DateFormat) ? "MMM DD, YYYY" : _model.DateFormat, string.IsNullOrWhiteSpace(_model.DateFormat) ? "MMM dd, yyyy" : _model.DateFormat);
                _dateFormat.Items.Insert(0, formatMatch);
            }

            _dateFormat.SelectedItem = formatMatch;
            _dateFormatLabel = Label("Format");
            _dateFormatLabel.VerticalAlignment = VerticalAlignment.Center;
            Form.Children.Add(FieldRow(_dateFormatLabel, _dateFormat));
            _showWeekday = new CheckBox { Content = "Show day name", IsChecked = _model.ShowWeekday, VerticalAlignment = VerticalAlignment.Center };
            Form.Children.Add(_showWeekday);
            _dateFont = FontCombo(_model.DateFontFamily);
            var dateFontLabel = Label("Font");
            dateFontLabel.VerticalAlignment = VerticalAlignment.Center;
            Form.Children.Add(FieldRow(dateFontLabel, _dateFont));
            SyncDateOptions();
        }

        if (_model.Kind == PanelKind.Weather)
        {
            _weatherQuery = new TextBox { PlaceholderText = "Search for a city", Text = _model.City };
            var search = FlatIcon("\uE721", "Search", async (_, _) => await SearchCitiesAsync());
            var weatherLabel = Label("City name");
            weatherLabel.VerticalAlignment = VerticalAlignment.Center;
            _places = new ListBox { Height = 140, DisplayMemberPath = nameof(GeoPlace.Label) };
            Form.Children.Add(FieldRow(weatherLabel, ActionRow(_weatherQuery, search)));
            Form.Children.Add(_places);
            Form.Children.Add(Label("Temperature"));
            _unit = new ComboBox { MaxHeight = 36 };
            _unit.Items.Add("Fahrenheit");
            _unit.Items.Add("Celsius");
            _unit.SelectedIndex = _model.TemperatureUnit.Equals("C", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            Form.Children.Add(_unit);
            _sevenDay = new CheckBox { Content = "Show the next 7 days", IsChecked = _model.ShowSevenDay };
            Form.Children.Add(_sevenDay);
        }

        _startWithWindows.IsChecked = _startupEnabled;
        _startWithWindows.VerticalAlignment = VerticalAlignment.Center;
        _startWithWindows.Checked += OnStartupChanged;
        _startWithWindows.Unchecked += OnStartupChanged;
        Form.Children.Add(_startWithWindows);

        Actions.Children.Add(FlatIcon("\uE711", "Cancel", (_, _) => Cancel()));
        Actions.Children.Add(FlatIcon("\uE74E", "Save", (_, _) => SaveAndClose()));
        HookLiveUpdates();
    }

    void HookLiveUpdates()
    {
        _showTitle.Checked += (_, _) => ApplyLive(false);
        _showTitle.Unchecked += (_, _) => ApplyLive(false);
        _title.TextChanged += (_, _) => ApplyLive(false);
        _interval?.RegisterPropertyChangedCallback(NumberBox.ValueProperty, (_, _) => ApplyLive(true));
        if (_fit is not null)
        {
            _fit.SelectionChanged += (_, _) => ApplyLive(true);
        }

        if (_city is not null)
        {
            _city.TextChanged += (_, _) => ApplyLive(true);
        }

        if (_zones is not null)
        {
            _zones.SelectionChanged += (_, _) => ApplyLive(true);
        }

        if (_hourFormat is not null)
        {
            _hourFormat.SelectionChanged += (_, _) => ApplyLive(true);
        }

        if (_clockFace is not null)
        {
            _clockFace.SelectionChanged += (_, _) => ApplyLive(true);
        }

        if (_showSeconds is not null)
        {
            _showSeconds.Checked += (_, _) => ApplyLive(true);
            _showSeconds.Unchecked += (_, _) => ApplyLive(true);
        }

        if (_showCity is not null)
        {
            _showCity.Checked += (_, _) => ApplyLive(true);
            _showCity.Unchecked += (_, _) => ApplyLive(true);
        }

        if (_clockFont is not null)
        {
            _clockFont.SelectionChanged += (_, _) => ApplyLive(true);
        }

        if (_dateFace is not null)
        {
            _dateFace.SelectionChanged += (_, _) =>
            {
                SyncDateOptions();
                ApplyLive(true);
            };
        }

        if (_dateFormat is not null)
        {
            _dateFormat.SelectionChanged += (_, _) => ApplyLive(true);
        }

        if (_showWeekday is not null)
        {
            _showWeekday.Checked += (_, _) => ApplyLive(true);
            _showWeekday.Unchecked += (_, _) => ApplyLive(true);
        }

        if (_dateFont is not null)
        {
            _dateFont.SelectionChanged += (_, _) => ApplyLive(true);
        }

        if (_places is not null)
        {
            _places.SelectionChanged += (_, _) => ApplyLive(true);
        }

        if (_unit is not null)
        {
            _unit.SelectionChanged += (_, _) => ApplyLive(true);
        }

        _sevenDay?.RegisterPropertyChangedCallback(CheckBox.IsCheckedProperty, (_, _) => ApplyLive(true));
    }

    void AddFolderPicker()
    {
        Form.Children.Add(Label(_model.Kind == PanelKind.Slideshow ? "Image folder" : "Folder"));
        _folder = new TextBox { Text = _model.FolderPath ?? "", IsReadOnly = true };
        var browse = FlatIcon("\uE838", "Browse", async (_, _) =>
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _ownerHwnd);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            var path = folder.Path;
            RunOnUi(() =>
            {
                if (_folder is null)
                {
                    return;
                }

                _folder.Text = path;
                ApplyLive(true);
            });
        });
        Form.Children.Add(ActionRow(_folder, browse));
    }

    static Grid FieldRow(FrameworkElement leading, FrameworkElement trailing)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (leading is Slider)
        {
            row.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            row.ColumnDefinitions[1].Width = GridLength.Auto;
        }

        Grid.SetColumn(trailing, 1);
        trailing.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(leading);
        row.Children.Add(trailing);
        return row;
    }

    static Grid ActionRow(FrameworkElement field, Button action)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(action, 1);
        action.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(field);
        row.Children.Add(action);
        return row;
    }

    Button FlatIcon(string glyph, string tip, RoutedEventHandler onClick)
    {
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 15,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Foreground = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255))
        };
        var button = new Button
        {
            Content = icon,
            Width = 32,
            Height = 32,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255)),
            Style = (Style)Resources["FlatIconButton"],
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(button, tip);
        button.PointerEntered += (_, _) =>
        {
            button.Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255));
            icon.Foreground = new SolidColorBrush(Color.FromArgb(255, 22, 22, 22));
        };
        button.PointerExited += (_, _) =>
        {
            button.Background = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
            icon.Foreground = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255));
        };
        button.Click += onClick;
        return button;
    }

    async Task SearchCitiesAsync()
    {
        if (_places is null || _weatherQuery is null)
        {
            return;
        }

        _places.Items.Clear();
        try
        {
            var results = await WeatherClient.SearchAsync(_weatherQuery.Text);
            RunOnUi(() =>
            {
                _results = results;
                if (_places is null)
                {
                    return;
                }

                _places.Items.Clear();
                foreach (var place in _results)
                {
                    _places.Items.Add(place);
                }
            });
        }
        catch
        {
            RunOnUi(() => _places?.Items.Add("Search failed. Check the connection and try again."));
        }
    }

    void RunOnUi(Action action)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        DispatcherQueue.TryEnqueue(() => action());
    }

    void ApplyLive(bool refreshContent)
    {
        WriteToModel();
        PanelManager.Save();
        _preview(refreshContent);
    }

    void OnStartupChanged(object sender, RoutedEventArgs e)
    {
        if (_ignoreStartup)
        {
            return;
        }

        var enabled = _startWithWindows.IsChecked == true;
        try
        {
            _startupTouched = true;
            WindowsStartup.SetEnabled(enabled);
        }
        catch
        {
            _ignoreStartup = true;
            _startWithWindows.IsChecked = !enabled;
            _ignoreStartup = false;
        }
    }

    void Cancel()
    {
        Restore();
        if (_startupTouched)
        {
            _ignoreStartup = true;
            _startWithWindows.IsChecked = _startupEnabled;
            _ignoreStartup = false;
            try
            {
                WindowsStartup.Restore(_startupSnapshot);
            }
            catch
            {
                // Leave the registration as the last successful change.
            }
        }

        PanelManager.Save();
        _preview(true);
        _close();
    }

    void SaveAndClose()
    {
        WriteToModel();
        PanelManager.Save();
        _preview(true);
        _close();
    }

    void WriteToModel()
    {
        _model.ShowTitle = _showTitle.IsChecked == true;
        _model.Title = _title.Text.Trim();
        _model.TintColor = _tint;
        _model.Opacity = _opacity.Value;
        if (_folder is not null)
        {
            _model.FolderPath = string.IsNullOrWhiteSpace(_folder.Text) ? null : _folder.Text.Trim();
        }

        if (_interval is not null)
        {
            _model.SlideshowIntervalSeconds = (int)Math.Clamp(double.IsNaN(_interval.Value) ? 6 : _interval.Value, 2, 3600);
        }

        if (_fit?.SelectedItem is string fit)
        {
            _model.SlideshowFit = fit;
        }

        if (_city is not null)
        {
            _model.City = _city.Text.Trim();
        }

        if (_zones?.SelectedItem is ZoneChoice zone)
        {
            _model.TimeZoneId = zone.Id;
        }

        if (_hourFormat is not null)
        {
            _model.Use24Hour = _hourFormat.SelectedIndex != 0;
        }

        if (_clockFace?.SelectedItem is string face)
        {
            _model.ClockFace = face;
        }

        if (_showSeconds is not null)
        {
            _model.ShowSeconds = _showSeconds.IsChecked == true;
        }

        if (_showCity is not null)
        {
            _model.ShowCity = _showCity.IsChecked == true;
        }

        if (_clockFont?.SelectedItem is string fontName)
        {
            _model.ClockFontFamily = fontName;
        }

        if (_dateFace?.SelectedItem is string dateFace)
        {
            _model.DateFace = dateFace;
        }

        if (_dateFormat?.SelectedItem is DateFormatChoice dateFormat)
        {
            _model.DateFormat = dateFormat.Pattern;
        }

        if (_showWeekday is not null)
        {
            _model.ShowWeekday = _showWeekday.IsChecked == true;
        }

        if (_dateFont?.SelectedItem is string dateFontName)
        {
            _model.DateFontFamily = dateFontName;
        }

        if (_places?.SelectedItem is GeoPlace place)
        {
            _model.City = place.City;
            _model.Latitude = place.Latitude;
            _model.Longitude = place.Longitude;
            _model.WeatherCache = null;
        }
        else if (_weatherQuery is not null && _model.Latitude is null)
        {
            _model.City = _weatherQuery.Text.Trim();
        }

        if (_unit is not null)
        {
            var next = _unit.SelectedIndex == 1 ? "C" : "F";
            if (!next.Equals(_model.TemperatureUnit, StringComparison.OrdinalIgnoreCase))
            {
                _model.WeatherCache = null;
            }

            _model.TemperatureUnit = next;
        }

        if (_sevenDay is not null)
        {
            _model.ShowSevenDay = _sevenDay.IsChecked == true;
        }
    }

    void Capture()
    {
        _snapshot.ShowTitle = _model.ShowTitle;
        _snapshot.Title = _model.Title;
        _snapshot.TintColor = _model.TintColor;
        _snapshot.Opacity = _model.Opacity;
        _snapshot.FolderPath = _model.FolderPath;
        _snapshot.SlideshowIntervalSeconds = _model.SlideshowIntervalSeconds;
        _snapshot.SlideshowFit = _model.SlideshowFit;
        _snapshot.TimeZoneId = _model.TimeZoneId;
        _snapshot.Use24Hour = _model.Use24Hour;
        _snapshot.ShowSeconds = _model.ShowSeconds;
        _snapshot.ShowCity = _model.ShowCity;
        _snapshot.ClockFontFamily = _model.ClockFontFamily;
        _snapshot.ClockFontSize = _model.ClockFontSize;
        _snapshot.ClockFace = _model.ClockFace;
        _snapshot.DateFace = _model.DateFace;
        _snapshot.DateFormat = _model.DateFormat;
        _snapshot.ShowWeekday = _model.ShowWeekday;
        _snapshot.DateFontFamily = _model.DateFontFamily;
        _snapshot.City = _model.City;
        _snapshot.Latitude = _model.Latitude;
        _snapshot.Longitude = _model.Longitude;
        _snapshot.TemperatureUnit = _model.TemperatureUnit;
        _snapshot.ShowSevenDay = _model.ShowSevenDay;
        _snapshot.WeatherCache = _model.WeatherCache;
    }

    void Restore()
    {
        _model.ShowTitle = _snapshot.ShowTitle;
        _model.Title = _snapshot.Title;
        _model.TintColor = _snapshot.TintColor;
        _model.Opacity = _snapshot.Opacity;
        _model.FolderPath = _snapshot.FolderPath;
        _model.SlideshowIntervalSeconds = _snapshot.SlideshowIntervalSeconds;
        _model.SlideshowFit = _snapshot.SlideshowFit;
        _model.TimeZoneId = _snapshot.TimeZoneId;
        _model.Use24Hour = _snapshot.Use24Hour;
        _model.ShowSeconds = _snapshot.ShowSeconds;
        _model.ShowCity = _snapshot.ShowCity;
        _model.ClockFontFamily = _snapshot.ClockFontFamily;
        _model.ClockFontSize = _snapshot.ClockFontSize;
        _model.ClockFace = _snapshot.ClockFace;
        _model.DateFace = _snapshot.DateFace;
        _model.DateFormat = _snapshot.DateFormat;
        _model.ShowWeekday = _snapshot.ShowWeekday;
        _model.DateFontFamily = _snapshot.DateFontFamily;
        _model.City = _snapshot.City;
        _model.Latitude = _snapshot.Latitude;
        _model.Longitude = _snapshot.Longitude;
        _model.TemperatureUnit = _snapshot.TemperatureUnit;
        _model.ShowSevenDay = _snapshot.ShowSevenDay;
        _model.WeatherCache = _snapshot.WeatherCache;
    }

    void MarkSwatches()
    {
        foreach (var button in _swatches)
        {
            var selected = button.Tag is string hex && hex.Equals(_tint, StringComparison.OrdinalIgnoreCase);
            button.Opacity = selected ? 1 : 0.45;
        }
    }

    void SyncDateOptions()
    {
        var calendar = _dateFace?.SelectedItem as string == "Calendar";
        if (_dateFormat is not null)
        {
            _dateFormat.IsEnabled = !calendar;
        }

        if (_dateFormatLabel is not null)
        {
            _dateFormatLabel.Opacity = calendar ? 0.45 : 1;
        }

        if (_showWeekday is not null)
        {
            _showWeekday.IsEnabled = !calendar;
        }
    }

    ComboBox FontCombo(string selected)
    {
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxHeight = 36, MinWidth = 160, MaxDropDownHeight = 280 };
        var chosen = string.IsNullOrWhiteSpace(selected) ? "Segoe UI Variable Display" : selected;
        foreach (var family in new InstalledFontCollection().Families.Select(font => font.Name).Distinct().OrderBy(name => name))
        {
            combo.Items.Add(family);
        }

        if (!combo.Items.OfType<string>().Any(name => name.Equals(chosen, StringComparison.OrdinalIgnoreCase)))
        {
            combo.Items.Insert(0, chosen);
        }

        combo.SelectedItem = combo.Items.OfType<string>().First(name => name.Equals(chosen, StringComparison.OrdinalIgnoreCase));
        return combo;
    }

    static TextBlock Label(string text) => new()
    {
        Text = text,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
    };

    static readonly DateFormatChoice[] DateFormats =
    [
        new("MMM DD, YYYY", "MMM dd, yyyy"),
        new("MMM D, YYYY", "MMM d, yyyy"),
        new("MMMM D, YYYY", "MMMM d, yyyy"),
        new("DD MMM YYYY", "dd MMM yyyy"),
        new("D MMMM YYYY", "d MMMM yyyy"),
        new("MM/DD/YYYY", "MM/dd/yyyy"),
        new("DD/MM/YYYY", "dd/MM/yyyy"),
        new("YYYY-MM-DD", "yyyy-MM-dd"),
        new("dddd, MMMM D", "dddd, MMMM d")
    ];

    sealed record DateFormatChoice(string Label, string Pattern)
    {
        public override string ToString() => Label;
    }

    sealed record ZoneChoice(string? Id, string Label)
    {
        public override string ToString() => Label;
    }
}
