using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopPanels.Models;

namespace DesktopPanels.Services;

public static class LayoutStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopPanels");

    public static string FilePath => Path.Combine(DirectoryPath, "layout.json");

    public static LayoutDocument Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return CreateDefault();
            }

            var json = File.ReadAllText(FilePath);
            var doc = JsonSerializer.Deserialize<LayoutDocument>(json, JsonOptions);
            if (doc?.Panels is null || doc.Panels.Count == 0)
            {
                return CreateDefault();
            }

            EnsureDefaultIconPanel(doc);
            return doc;
        }
        catch
        {
            return CreateDefault();
        }
    }

    public static void Save(LayoutDocument document)
    {
        Directory.CreateDirectory(DirectoryPath);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(FilePath, json);
    }

    public static LayoutDocument CreateDefault()
    {
        return new LayoutDocument
        {
            Panels =
            [
                new PanelModel
                {
                    Kind = PanelKind.Icons,
                    IsDefault = true,
                    Title = "Desktop",
                    ShowTitle = true,
                    X = 96,
                    Y = 72,
                    Width = 520,
                    Height = 460,
                    TintColor = "#2F6FED"
                }
            ]
        };
    }

    public static void EnsureDefaultIconPanel(LayoutDocument document)
    {
        var iconPanels = document.Panels.Where(p => p.Kind == PanelKind.Icons).ToList();
        if (iconPanels.Count == 0)
        {
            document.Panels.Insert(0, new PanelModel
            {
                Kind = PanelKind.Icons,
                IsDefault = true,
                Title = "Desktop",
                ShowTitle = true,
                X = 96,
                Y = 72,
                Width = 520,
                Height = 460
            });
            return;
        }

        if (iconPanels.All(p => !p.IsDefault))
        {
            iconPanels[0].IsDefault = true;
        }
    }
}
