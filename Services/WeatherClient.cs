using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using DesktopPanels.Models;

namespace DesktopPanels.Services;

public static class WeatherClient
{
    static readonly HttpClient Http = CreateClient();

    public static async Task<IReadOnlyList<GeoPlace>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var url = "https://geocoding-api.open-meteo.com/v1/search?count=5&language=en&format=json&name="
            + Uri.EscapeDataString(query.Trim());
        using var doc = await GetJsonAsync(url, cancellationToken);
        if (!doc.RootElement.TryGetProperty("results", out var results))
        {
            return [];
        }

        var places = new List<GeoPlace>();
        foreach (var item in results.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString() ?? "";
            var admin = item.TryGetProperty("admin1", out var adminEl) ? adminEl.GetString() : null;
            var country = item.TryGetProperty("country", out var countryEl) ? countryEl.GetString() : null;
            var label = string.Join(", ", new[] { name, admin, country }.Where(part => !string.IsNullOrWhiteSpace(part)));
            places.Add(new GeoPlace
            {
                Label = label,
                City = name,
                Latitude = item.GetProperty("latitude").GetDouble(),
                Longitude = item.GetProperty("longitude").GetDouble()
            });
        }

        return places;
    }

    public static async Task<WeatherSnapshot?> ForecastAsync(double latitude, double longitude, string unit, string city, CancellationToken cancellationToken = default)
    {
        var temperatureUnit = unit.Equals("C", StringComparison.OrdinalIgnoreCase) ? "celsius" : "fahrenheit";
        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min&temperature_unit={temperatureUnit}&timezone=auto&forecast_days=7");
        using var doc = await GetJsonAsync(url, cancellationToken);
        var current = doc.RootElement.GetProperty("current");
        var daily = doc.RootElement.GetProperty("daily");
        var dates = daily.GetProperty("time");
        var codes = daily.GetProperty("weather_code");
        var highs = daily.GetProperty("temperature_2m_max");
        var lows = daily.GetProperty("temperature_2m_min");
        var days = new List<WeatherDay>();
        var count = Math.Min(7, dates.GetArrayLength());
        for (var i = 0; i < count; i++)
        {
            days.Add(new WeatherDay
            {
                Date = dates[i].GetString() ?? "",
                WeatherCode = codes[i].GetInt32(),
                High = highs[i].GetDouble(),
                Low = lows[i].GetDouble()
            });
        }

        return new WeatherSnapshot
        {
            City = city,
            Temperature = current.GetProperty("temperature_2m").GetDouble(),
            WeatherCode = current.GetProperty("weather_code").GetInt32(),
            Unit = unit.Equals("C", StringComparison.OrdinalIgnoreCase) ? "C" : "F",
            Days = days
        };
    }

    public static string Describe(int code) => code switch
    {
        0 => "Clear",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 or 56 or 57 => "Drizzle",
        61 or 63 or 65 or 66 or 67 => "Rain",
        71 or 73 or 75 or 77 => "Snow",
        80 or 81 or 82 => "Showers",
        85 or 86 => "Snow showers",
        95 or 96 or 99 => "Thunderstorm",
        _ => "Weather"
    };

    public static WeatherArtKind ArtFor(int code) => code switch
    {
        0 or 1 => WeatherArtKind.Clear,
        2 => WeatherArtKind.Partly,
        3 => WeatherArtKind.Cloud,
        45 or 48 => WeatherArtKind.Fog,
        51 or 53 or 55 or 56 or 57 => WeatherArtKind.Drizzle,
        71 or 73 or 75 or 77 or 85 or 86 => WeatherArtKind.Snow,
        95 or 96 or 99 => WeatherArtKind.Thunder,
        _ => WeatherArtKind.Rain
    };

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DesktopPanels", "1.0"));
        return client;
    }

    static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        var json = await Http.GetStringAsync(url, cancellationToken);
        return JsonDocument.Parse(json);
    }
}

public enum WeatherArtKind
{
    Clear,
    Partly,
    Cloud,
    Fog,
    Drizzle,
    Rain,
    Snow,
    Thunder
}
