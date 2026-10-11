using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace DesktopPanels.Services;

static class WindowsStartup
{
    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ShortcutName = "Desktop Panels.lnk";
    const string ExeName = "DesktopPanels.exe";

    public sealed class Snapshot
    {
        public byte[]? Canonical { get; init; }
        public List<(string Path, byte[] Bytes)> Others { get; init; } = [];
        public List<(string Name, string Value)> Run { get; init; } = [];
    }

    public static Snapshot Capture()
    {
        byte[]? canonical = null;
        if (File.Exists(ShortcutPath) && MentionsApp(ShortcutPath))
        {
            canonical = File.ReadAllBytes(ShortcutPath);
        }

        var others = new List<(string Path, byte[] Bytes)>();
        foreach (var path in StartupShortcuts())
        {
            if (path.Equals(ShortcutPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            others.Add((path, File.ReadAllBytes(path)));
        }

        return new Snapshot
        {
            Canonical = canonical,
            Others = others,
            Run = RunValues().ToList()
        };
    }

    public static void Restore(Snapshot snapshot)
    {
        SetEnabled(false);
        if (snapshot.Canonical is not null)
        {
            Directory.CreateDirectory(StartupFolder);
            File.WriteAllBytes(ShortcutPath, snapshot.Canonical);
        }

        foreach (var (path, bytes) in snapshot.Others)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        if (snapshot.Run.Count == 0)
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        foreach (var (name, value) in snapshot.Run)
        {
            key.SetValue(name, value);
        }
    }

    public static bool IsEnabled()
    {
        if (File.Exists(ShortcutPath) && MentionsApp(ShortcutPath))
        {
            return true;
        }

        if (StartupShortcuts().Any(path => !path.Equals(ShortcutPath, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return RunEntries().Any();
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            CreateShortcut();
            RemoveRunEntries();
            foreach (var extra in StartupShortcuts().Where(path => !path.Equals(ShortcutPath, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                File.Delete(extra);
            }

            return;
        }

        RemoveRunEntries();
        if (File.Exists(ShortcutPath) && MentionsApp(ShortcutPath))
        {
            File.Delete(ShortcutPath);
        }

        foreach (var extra in StartupShortcuts().ToList())
        {
            File.Delete(extra);
        }
    }

    static string ShortcutPath => Path.Combine(StartupFolder, ShortcutName);

    static string StartupFolder => Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    static string ExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }

        return Path.Combine(AppContext.BaseDirectory, ExeName);
    }

    static void CreateShortcut()
    {
        var exe = ExecutablePath();
        Directory.CreateDirectory(StartupFolder);
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is unavailable.");
        var shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Windows Script Host is unavailable.");
        var shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [ShortcutPath])
            ?? throw new InvalidOperationException("Could not create the startup shortcut.");
        var shortcutType = shortcut.GetType();
        shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [exe]);
        shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, [Path.GetDirectoryName(exe) ?? ""]);
        shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, [exe + ",0"]);
        shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, ["Desktop Panels"]);
        shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
    }

    static IEnumerable<string> StartupShortcuts()
    {
        if (!Directory.Exists(StartupFolder))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(StartupFolder, "*.lnk"))
        {
            if (MentionsApp(path))
            {
                yield return path;
            }
        }
    }

    static bool MentionsApp(string path)
    {
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            return false;
        }

        var utf16 = Encoding.Unicode.GetBytes(ExeName);
        var ascii = Encoding.ASCII.GetBytes(ExeName);
        return IndexOf(data, utf16) >= 0 || IndexOf(data, ascii) >= 0;
    }

    static IEnumerable<string> RunEntries()
    {
        return RunValues().Select(entry => entry.Name);
    }

    static IEnumerable<(string Name, string Value)> RunValues()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        if (key is null)
        {
            yield break;
        }

        foreach (var name in key.GetValueNames())
        {
            if (key.GetValue(name) is string value && value.Contains(ExeName, StringComparison.OrdinalIgnoreCase))
            {
                yield return (name, value);
            }
        }
    }

    static void RemoveRunEntries()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            return;
        }

        foreach (var name in key.GetValueNames())
        {
            var value = key.GetValue(name) as string;
            if (value?.Contains(ExeName, StringComparison.OrdinalIgnoreCase) == true)
            {
                key.DeleteValue(name, throwOnMissingValue: false);
            }
        }
    }

    static int IndexOf(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return -1;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
