using Microsoft.UI.Xaml;

namespace DesktopPanels;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, _) => Services.DesktopHost.ShowIcons();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services.PanelManager.Start();
    }
}
