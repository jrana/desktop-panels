using System.Diagnostics;

namespace DesktopPanels.Services;

public static class Watchdog
{
    public static void Spawn()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--watch {Environment.ProcessId}",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            // The tray command can still restore icons if the watcher does not start.
        }
    }

    public static void WaitAndRestore(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit();
        }
        catch
        {
            // The parent already exited.
        }

        DesktopHost.ShowIcons();
    }
}
