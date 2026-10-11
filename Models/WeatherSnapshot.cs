namespace DesktopPanels.Models;

public sealed class WeatherSnapshot
{
    public string City { get; set; } = "";
    public double Temperature { get; set; }
    public int WeatherCode { get; set; }
    public string Unit { get; set; } = "F";
    public List<WeatherDay> Days { get; set; } = [];
}

public sealed class WeatherDay
{
    public string Date { get; set; } = "";
    public int WeatherCode { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
}

public sealed class GeoPlace
{
    public string Label { get; init; } = "";
    public string City { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}
