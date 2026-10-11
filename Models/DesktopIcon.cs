using Microsoft.UI.Xaml.Media.Imaging;

namespace DesktopPanels.Models;

public sealed class DesktopIcon
{
    public required string ParsingName { get; init; }
    public required string DisplayName { get; init; }
    public BitmapImage? Icon { get; init; }
}
