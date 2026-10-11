using System.Globalization;
using System.Text.Json;
using DesktopPanels.Models;
using DesktopPanels.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DesktopPanels.Views;

public sealed partial class WeatherView : UserControl
{
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    readonly DispatcherQueueTimer _timer;
    PanelModel? _model;
    int _request;

    public WeatherView()
    {
        InitializeComponent();
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMinutes(20);
        _timer.Tick += async (_, _) => await RefreshAsync();
        Unloaded += (_, _) => _timer.Stop();
    }

    public void Load(PanelModel model)
    {
        _model = model;
        ShowCache();
        _ = RefreshAsync();
        _timer.Start();
    }

    void ShowCache()
    {
        if (_model is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_model.WeatherCache))
        {
            Art.SetKind(WeatherArtKind.Partly);
            TempText.Text = "--";
            ConditionText.Text = "";
            CityText.Text = _model.City;
            Week.Children.Clear();
            Week.Visibility = Visibility.Collapsed;
            StatusText.Text = _model.Latitude is null
                ? "Choose a city in panel settings."
                : "Loading weather…";
            return;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<WeatherSnapshot>(_model.WeatherCache, JsonOptions);
            if (snapshot is not null)
            {
                Apply(snapshot, stale: true);
            }
        }
        catch
        {
            StatusText.Text = "Saved weather could not be read.";
        }
    }

    async Task RefreshAsync()
    {
        if (_model?.Latitude is null || _model.Longitude is null)
        {
            ShowCache();
            return;
        }

        var token = ++_request;
        var latitude = _model.Latitude.Value;
        var longitude = _model.Longitude.Value;
        var unit = _model.TemperatureUnit;
        var city = string.IsNullOrWhiteSpace(_model.City) ? "Weather" : _model.City;
        try
        {
            var snapshot = await WeatherClient.ForecastAsync(latitude, longitude, unit, city);
            RunOnUi(() =>
            {
                if (token != _request || snapshot is null || _model is null)
                {
                    return;
                }

                _model.WeatherCache = JsonSerializer.Serialize(snapshot, JsonOptions);
                PanelManager.Save();
                Apply(snapshot, stale: false);
            });
        }
        catch
        {
            RunOnUi(() =>
            {
                if (token != _request)
                {
                    return;
                }

                StatusText.Text = string.IsNullOrWhiteSpace(_model?.WeatherCache)
                    ? "Weather is unavailable right now."
                    : "Showing the last saved reading.";
            });
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

    void Apply(WeatherSnapshot snapshot, bool stale)
    {
        Art.SetKind(WeatherClient.ArtFor(snapshot.WeatherCode));
        TempText.Text = $"{snapshot.Temperature.ToString("0", CultureInfo.CurrentCulture)}°{snapshot.Unit}";
        ConditionText.Text = WeatherClient.Describe(snapshot.WeatherCode);
        CityText.Text = string.IsNullOrWhiteSpace(snapshot.City) ? _model?.City : snapshot.City;
        Week.Children.Clear();
        var showWeek = _model?.ShowSevenDay == true && snapshot.Days.Count > 0;
        Week.Visibility = showWeek ? Visibility.Visible : Visibility.Collapsed;
        if (showWeek)
        {
            foreach (var day in snapshot.Days)
            {
                Week.Children.Add(DayColumn(day));
            }
        }

        StatusText.Text = stale ? "Showing the last saved reading." : "";
    }

    static UIElement DayColumn(WeatherDay day)
    {
        DateTime date = DateTime.TryParse(day.Date, out var parsed) ? parsed : DateTime.Today;
        var art = new WeatherArt();
        art.SetKind(WeatherClient.ArtFor(day.WeatherCode));
        var icon = new Viewbox { Width = 32, Height = 32, Child = art };
        return new StackPanel
        {
            Width = 48,
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = date.ToString("ddd"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                icon,
                new TextBlock
                {
                    Text = day.High.ToString("0", CultureInfo.CurrentCulture) + "°",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = day.Low.ToString("0", CultureInfo.CurrentCulture) + "°",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 255, 255, 255)),
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            }
        };
    }
}
