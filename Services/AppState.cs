namespace DesktopPanels.Services;

public static class AppState
{
    public static bool IsEditing { get; private set; }
    public static bool DesktopIconsHidden { get; set; }

    public static event Action? EditingChanged;
    public static event Action? LayoutChanged;
    public static event Action? IconsChanged;

    public static void SetEditing(bool value)
    {
        if (IsEditing == value)
        {
            return;
        }

        IsEditing = value;
        EditingChanged?.Invoke();
    }

    public static void NotifyLayoutChanged() => LayoutChanged?.Invoke();

    public static void NotifyIconsChanged() => IconsChanged?.Invoke();
}
