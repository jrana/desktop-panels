namespace DesktopPanels;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--watch" && int.TryParse(args[1], out var pid))
        {
            Services.Watchdog.WaitAndRestore(pid);
            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(_ => new App());
    }
}
