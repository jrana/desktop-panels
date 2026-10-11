using DesktopPanels.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DesktopPanels.Views;

public sealed partial class ClockView : UserControl
{
    readonly DispatcherQueueTimer _timer;
    PanelModel? _model;
    bool _fitting;
    string _fitKey = "";

    public ClockView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Fit(force: true);
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(200);
        _timer.Tick += (_, _) => Render();
        _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    public void Load(PanelModel model)
    {
        _model = model;
        _fitKey = "";
        Render();
    }

    void Render()
    {
        if (_model is null)
        {
            return;
        }

        DateTime time;
        string zoneLabel;
        if (string.IsNullOrWhiteSpace(_model.TimeZoneId))
        {
            time = DateTime.Now;
            zoneLabel = "Local";
        }
        else
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(_model.TimeZoneId);
                time = TimeZoneInfo.ConvertTime(DateTime.UtcNow, zone);
                zoneLabel = zone.DisplayName;
            }
            catch
            {
                time = DateTime.Now;
                zoneLabel = "Local";
            }
        }

        var family = string.IsNullOrWhiteSpace(_model.ClockFontFamily) ? "Segoe UI Variable Display" : _model.ClockFontFamily;
        var font = new FontFamily(family);
        var swiss = _model.ClockFace.Equals("Swiss", StringComparison.OrdinalIgnoreCase);
        DigitalRoot.Visibility = swiss ? Visibility.Collapsed : Visibility.Visible;
        SwissRoot.Visibility = swiss ? Visibility.Visible : Visibility.Collapsed;
        var interval = swiss && _model.ShowSeconds ? TimeSpan.FromMilliseconds(40) : TimeSpan.FromMilliseconds(200);
        if (_timer.Interval != interval)
        {
            _timer.Interval = interval;
        }

        if (swiss)
        {
            Face.SetTime(time, _model.ShowSeconds);
            SwissCity.FontFamily = font;
            SwissCity.Text = string.IsNullOrWhiteSpace(_model.City) ? zoneLabel : _model.City;
            SwissCity.Visibility = _model.ShowCity ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        TimeText.FontFamily = font;
        CityText.FontFamily = font;
        AmText.FontFamily = font;
        PmText.FontFamily = font;
        var pattern = _model.Use24Hour
            ? (_model.ShowSeconds ? "HH:mm:ss" : "HH:mm")
            : (_model.ShowSeconds ? "h:mm:ss" : "h:mm");
        TimeText.Text = time.ToString(pattern);
        CityText.Text = string.IsNullOrWhiteSpace(_model.City) ? zoneLabel : _model.City;
        CityText.Visibility = _model.ShowCity ? Visibility.Visible : Visibility.Collapsed;
        Meridiem.Visibility = _model.Use24Hour ? Visibility.Collapsed : Visibility.Visible;
        var afternoon = time.Hour >= 12;
        AmText.Opacity = afternoon ? 0.4 : 1;
        PmText.Opacity = afternoon ? 1 : 0.4;
        AmText.Foreground = TimeText.Foreground;
        PmText.Foreground = TimeText.Foreground;
        AmText.FontWeight = afternoon ? FontWeights.SemiBold : FontWeights.Bold;
        PmText.FontWeight = afternoon ? FontWeights.Bold : FontWeights.SemiBold;
        Fit(force: false);
    }

    void Fit(bool force)
    {
        if (_fitting || _model is null || ActualWidth < 40 || ActualHeight < 40)
        {
            return;
        }

        var sample = _model.Use24Hour
            ? (_model.ShowSeconds ? "00:00:00" : "00:00")
            : (_model.ShowSeconds ? "10:00:00" : "10:00");
        var key = $"{ActualWidth:0}|{ActualHeight:0}|{sample}|{CityText.Text}|{CityText.Visibility}|{_model.ClockFontFamily}|{Meridiem.Visibility}";
        if (!force && key == _fitKey)
        {
            return;
        }

        _fitting = true;
        var visible = TimeText.Text;
        try
        {
            TimeText.Text = sample;
            var width = Math.Max(40, ActualWidth - 32);
            var height = Math.Max(40, ActualHeight - 16);
            double low = 12;
            double high = Math.Max(48, height);
            double best = low;
            for (var step = 0; step < 14; step++)
            {
                var mid = (low + high) / 2;
                ApplySize(mid);
                TimeRow.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                var cityHeight = 0d;
                var cityWidth = 0d;
                if (CityText.Visibility == Visibility.Visible)
                {
                    CityText.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
                    cityHeight = CityText.DesiredSize.Height + 4;
                    cityWidth = CityText.DesiredSize.Width;
                }

                var neededWidth = Math.Max(TimeRow.DesiredSize.Width, cityWidth);
                var neededHeight = TimeRow.DesiredSize.Height + cityHeight;
                if (neededWidth <= width && neededHeight <= height)
                {
                    best = mid;
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            ApplySize(best);
            _fitKey = key;
        }
        finally
        {
            TimeText.Text = visible;
            _fitting = false;
        }
    }

    void ApplySize(double timeSize)
    {
        TimeText.FontSize = timeSize;
        var mark = Math.Max(14, Math.Round(timeSize * 0.38));
        AmText.FontSize = mark;
        PmText.FontSize = mark;
        CityText.FontSize = Math.Max(12, Math.Round(timeSize * 0.28));
    }
}
