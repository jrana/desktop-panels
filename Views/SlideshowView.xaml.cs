using DesktopPanels.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DesktopPanels.Views;

public sealed partial class SlideshowView : UserControl
{
    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"
    };

    readonly DispatcherQueueTimer _timer;
    PanelModel? _model;
    List<string> _files = [];
    int _index;
    bool _showingA = true;
    bool _fading;

    public SlideshowView()
    {
        InitializeComponent();
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Tick += (_, _) => Advance();
        Unloaded += (_, _) => _timer.Stop();
    }

    public void Load(PanelModel model)
    {
        _model = model;
        PhotoA.Stretch = StretchFor(model);
        PhotoB.Stretch = StretchFor(model);
        _files = LoadFiles(model.FolderPath);
        _index = 0;
        _showingA = true;
        _fading = false;
        PhotoA.Opacity = 1;
        PhotoB.Opacity = 0;
        if (_files.Count == 0)
        {
            PhotoA.Source = null;
            PhotoB.Source = null;
            EmptyText.Visibility = Visibility.Visible;
            _timer.Stop();
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        PhotoA.Source = LoadImage(_files[0]);
        if (_files.Count == 1)
        {
            _timer.Stop();
            return;
        }

        _timer.Interval = TimeSpan.FromSeconds(Math.Clamp(model.SlideshowIntervalSeconds, 2, 3600));
        _timer.Start();
    }

    static Stretch StretchFor(PanelModel model) =>
        model.SlideshowFit.Equals("Contain", StringComparison.OrdinalIgnoreCase) ? Stretch.Uniform : Stretch.UniformToFill;

    static List<string> LoadFiles(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(folder)
                .Where(path => Extensions.Contains(Path.GetExtension(path)))
                .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    static BitmapImage LoadImage(string path)
    {
        var image = new BitmapImage();
        image.DecodePixelWidth = 1600;
        image.UriSource = new Uri(path);
        return image;
    }

    void Advance()
    {
        if (_fading || _files.Count < 2 || _model is null)
        {
            return;
        }

        var latest = LoadFiles(_model.FolderPath);
        if (latest.Count != _files.Count || !latest.SequenceEqual(_files, StringComparer.OrdinalIgnoreCase))
        {
            _files = latest;
            if (_files.Count == 0)
            {
                Load(_model);
                return;
            }
        }

        _index = (_index + 1) % _files.Count;
        var incoming = _showingA ? PhotoB : PhotoA;
        var outgoing = _showingA ? PhotoA : PhotoB;
        incoming.Source = LoadImage(_files[_index]);
        _fading = true;
        _showingA = !_showingA;
        Fade(outgoing, incoming);
    }

    void Fade(UIElement outgoing, UIElement incoming)
    {
        var queue = DispatcherQueue.GetForCurrentThread();
        var steps = 0;
        var timer = queue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(30);
        timer.Tick += (_, _) =>
        {
            steps++;
            var t = Math.Min(1, steps / 12d);
            incoming.Opacity = t;
            outgoing.Opacity = 1 - t;
            if (steps >= 12)
            {
                timer.Stop();
                _fading = false;
            }
        };
        timer.Start();
    }
}
