using Microsoft.UI.Windowing;

namespace DesktopPanels.Services;

static class AppIcon
{
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

    public static void Apply(AppWindow window)
    {
        if (File.Exists(FilePath))
        {
            window.SetIcon(FilePath);
        }
    }
}
