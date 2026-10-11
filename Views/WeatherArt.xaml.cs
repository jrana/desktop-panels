using DesktopPanels.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopPanels.Views;

public sealed partial class WeatherArt : UserControl
{
    public WeatherArt()
    {
        InitializeComponent();
        SetKind(WeatherArtKind.Clear);
    }

    public void SetKind(WeatherArtKind kind)
    {
        Sun.Visibility = kind is WeatherArtKind.Clear or WeatherArtKind.Partly ? Visibility.Visible : Visibility.Collapsed;
        var cloudy = kind is WeatherArtKind.Partly or WeatherArtKind.Cloud or WeatherArtKind.Drizzle or WeatherArtKind.Rain or WeatherArtKind.Snow or WeatherArtKind.Thunder;
        Cloud.Visibility = cloudy ? Visibility.Visible : Visibility.Collapsed;
        CloudSmall.Visibility = cloudy ? Visibility.Visible : Visibility.Collapsed;
        Drops.Visibility = kind is WeatherArtKind.Drizzle or WeatherArtKind.Rain ? Visibility.Visible : Visibility.Collapsed;
        Flakes.Visibility = kind == WeatherArtKind.Fog ? Visibility.Collapsed : kind == WeatherArtKind.Snow ? Visibility.Visible : Visibility.Collapsed;
        Bolt.Visibility = kind == WeatherArtKind.Thunder ? Visibility.Visible : Visibility.Collapsed;
        FogBands.Visibility = kind == WeatherArtKind.Fog ? Visibility.Visible : Visibility.Collapsed;
        if (kind == WeatherArtKind.Clear)
        {
            Sun.Width = 36;
            Sun.Height = 36;
            Sun.Margin = new Thickness(14, 12, 0, 0);
        }
        else
        {
            Sun.Width = 26;
            Sun.Height = 26;
            Sun.Margin = new Thickness(6, 4, 0, 0);
        }
    }
}
