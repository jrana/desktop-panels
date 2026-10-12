namespace DesktopPanels.Models;

public sealed class PanelModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public PanelKind Kind { get; set; }
    public bool IsDefault { get; set; }
    public int X { get; set; } = 80;
    public int Y { get; set; } = 80;
    public int Width { get; set; } = 460;
    public int Height { get; set; } = 420;
    public bool ShowTitle { get; set; } = true;
    public string Title { get; set; } = "";
    public string TintColor { get; set; } = "#2F6FED";
    public double Opacity { get; set; } = 0.55;
    public List<string> IconIds { get; set; } = [];
    public string? FolderPath { get; set; }
    public int SlideshowIntervalSeconds { get; set; } = 6;
    public string SlideshowFit { get; set; } = "Cover";
    public string? TimeZoneId { get; set; }
    public bool Use24Hour { get; set; } = true;
    public bool ShowSeconds { get; set; } = true;
    public bool ShowCity { get; set; } = true;
    public string ClockFontFamily { get; set; } = "Segoe UI Variable Display";
    public double ClockFontSize { get; set; } = 44;
    public string ClockFace { get; set; } = "Digital";
    public string DateFace { get; set; } = "Date";
    public string DateFormat { get; set; } = "MMM dd, yyyy";
    public bool ShowWeekday { get; set; } = true;
    public string DateFontFamily { get; set; } = "Segoe UI Variable Display";
    public string City { get; set; } = "";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string TemperatureUnit { get; set; } = "F";
    public bool ShowSevenDay { get; set; } = true;
    public string? WeatherCache { get; set; }
}
