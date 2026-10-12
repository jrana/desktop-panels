using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using DesktopPanels.Models;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DesktopPanels.Services;

public static class ShellDesktop
{
    const uint ShgfiIcon = 0x000000100;
    const uint ShgfiLargeIcon = 0x000000000;
    const string RecycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";

    public readonly record struct ListedIcon(string Path, string Name);

    static readonly object IconGate = new();
    static readonly Dictionary<string, byte[]?> PngCache = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, BitmapImage?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ListedIcon> ListDesktop()
    {
        var listed = CollectDesktop();
        foreach (var icon in listed)
        {
            PngFor(icon.Path);
        }

        return listed;
    }

    public static void PrefetchFolder(string? folder)
    {
        foreach (var icon in CollectFolder(folder))
        {
            PngFor(icon.Path);
        }
    }

    public static IReadOnlyList<DesktopIcon> Enumerate() => Materialize(ListDesktop());

    public static IReadOnlyList<DesktopIcon> Materialize(IReadOnlyList<ListedIcon> listed)
    {
        var results = new List<DesktopIcon>(listed.Count);
        foreach (var icon in listed)
        {
            results.Add(new DesktopIcon
            {
                ParsingName = icon.Path,
                DisplayName = icon.Name,
                Icon = IconFor(icon.Path)
            });
        }

        results.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.DisplayName, b.DisplayName));
        return results;
    }

    static List<ListedIcon> CollectDesktop()
    {
        var raw = new List<ListedIcon>();
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is not null)
            {
                dynamic shell = Activator.CreateInstance(type)!;
                dynamic folder = shell.NameSpace(0);
                foreach (dynamic item in folder.Items())
                {
                    try
                    {
                        string path = item.Path;
                        string name = item.Name;
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            raw.Add(new ListedIcon(path, string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : name));
                        }
                    }
                    catch
                    {
                        // Skip items the shell cannot name.
                    }
                    finally
                    {
                        try
                        {
                            Marshal.ReleaseComObject(item);
                        }
                        catch
                        {
                            // Already released.
                        }
                    }
                }

                Marshal.ReleaseComObject(folder);
                Marshal.ReleaseComObject(shell);
            }
        }
        catch
        {
            raw.Clear();
        }

        if (raw.Count == 0)
        {
            raw.AddRange(FallbackFiles());
        }

        var hidden = ReadHiddenSpecialIcons();
        var shown = ReadShownSpecialIcons();
        var desktopDirs = DesktopDirectories();
        var results = new List<ListedIcon>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var icon in raw)
        {
            if (!seen.Add(icon.Path) || !ShouldInclude(icon.Path, desktopDirs, hidden, shown))
            {
                continue;
            }

            results.Add(icon);
        }

        return results;
    }

    public static IReadOnlyList<DesktopIcon> EnumerateFolder(string? folder) => Materialize(CollectFolder(folder));

    static List<ListedIcon> CollectFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        var results = new List<ListedIcon>();
        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                var info = new FileInfo(path);
                if (info.Attributes.HasFlag(FileAttributes.Hidden) || info.Attributes.HasFlag(FileAttributes.System))
                {
                    continue;
                }

                results.Add(new ListedIcon(path, Path.GetFileName(path)));
            }
        }
        catch
        {
            return results;
        }

        return results;
    }

    public static void Open(string parsingName)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = parsingName,
            UseShellExecute = true
        });
    }

    static bool ShouldInclude(string path, IReadOnlyList<string> desktopDirs, HashSet<string> hiddenSpecial, HashSet<string> shownSpecial)
    {
        if (path.StartsWith("::", StringComparison.Ordinal))
        {
            var clsid = ExtractClsid(path);
            if (clsid is null || hiddenSpecial.Contains(clsid))
            {
                return false;
            }

            if (clsid.Equals("{645FF040-5081-101B-9F08-00AA002F954E}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return shownSpecial.Contains(clsid);
        }

        if (path.EndsWith("\\desktop.ini", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var dir in desktopDirs)
        {
            if (path.Equals(dir, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var prefix = dir.TrimEnd('\\') + "\\";
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var attrs = File.GetAttributes(path);
                    if (attrs.HasFlag(FileAttributes.Hidden) || attrs.HasFlag(FileAttributes.System))
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }

                return true;
            }
        }

        return false;
    }

    static List<ListedIcon> FallbackFiles()
    {
        var list = new List<ListedIcon>();
        foreach (var dir in DesktopDirectories())
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFileSystemEntries(dir))
            {
                list.Add(new ListedIcon(path, Path.GetFileName(path)));
            }
        }

        list.Add(new ListedIcon(RecycleBin, "Recycle Bin"));
        return list;
    }

    static IReadOnlyList<string> DesktopDirectories()
    {
        return
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        ];
    }

    static HashSet<string> ReadHiddenSpecialIcons()
    {
        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel");
            if (key is null)
            {
                return hidden;
            }

            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is int value && value != 0)
                {
                    hidden.Add(name);
                }
            }
        }
        catch
        {
            // Treat registry failures as "use defaults".
        }

        return hidden;
    }

    static HashSet<string> ReadShownSpecialIcons()
    {
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel");
            if (key is null)
            {
                return shown;
            }

            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is int value && value == 0)
                {
                    shown.Add(name);
                }
            }
        }
        catch
        {
            // Missing keys mean the Windows defaults.
        }

        return shown;
    }

    static string? ExtractClsid(string parsingName)
    {
        var start = parsingName.IndexOf('{');
        var end = parsingName.IndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return parsingName.Substring(start, end - start + 1);
    }

    static BitmapImage? IconFor(string path)
    {
        if (IconCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var image = ImageFromPng(PngFor(path));
        IconCache[path] = image;
        return image;
    }

    static byte[]? PngFor(string path)
    {
        lock (IconGate)
        {
            if (PngCache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var png = RenderPng(path);
            PngCache[path] = png;
            return png;
        }
    }

    static BitmapImage? ImageFromPng(byte[]? png)
    {
        if (png is null || png.Length == 0)
        {
            return null;
        }

        using var memory = new MemoryStream(png, writable: false);
        using var stream = memory.AsRandomAccessStream();
        var image = new BitmapImage();
        image.SetSource(stream);
        return image;
    }

    static byte[]? RenderPng(string path)
    {
        var info = new ShFileInfo();
        var handle = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiLargeIcon);
        if (handle == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            using var icon = System.Drawing.Icon.FromHandle(info.hIcon);
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr hIcon);
}
