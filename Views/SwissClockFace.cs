using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace DesktopPanels.Views;

public sealed class SwissClockFace : UserControl
{
    const double Size = 400;
    const double Center = 200;

    static readonly SolidColorBrush Ink = new(Color.FromArgb(255, 22, 22, 22));
    static readonly SolidColorBrush Dial = new(Color.FromArgb(255, 250, 248, 244));
    static readonly SolidColorBrush Second = new(Color.FromArgb(255, 214, 40, 40));

    readonly RotateTransform _hour;
    readonly RotateTransform _minute;
    readonly RotateTransform _second;
    readonly Canvas _secondLayer;

    public SwissClockFace()
    {
        var dial = new Canvas { Width = Size, Height = Size };
        dial.Children.Add(new Ellipse
        {
            Width = Size,
            Height = Size,
            Fill = Dial
        });
        var rim = new Ellipse
        {
            Width = Size - 10,
            Height = Size - 10,
            Stroke = Ink,
            StrokeThickness = 3
        };
        Canvas.SetLeft(rim, 5);
        Canvas.SetTop(rim, 5);
        dial.Children.Add(rim);

        for (var hour = 0; hour < 12; hour++)
        {
            dial.Children.Add(Index(hour * 30));
        }

        dial.Children.Add(Hand(14, 122, 104, Ink, out _hour));
        dial.Children.Add(Hand(8, 176, 154, Ink, out _minute));

        _secondLayer = new Canvas { Width = Size, Height = Size };
        var needle = new Rectangle
        {
            Width = 2.4,
            Height = 206,
            RadiusX = 1.2,
            RadiusY = 1.2,
            Fill = Second
        };
        Canvas.SetLeft(needle, Center - 1.2);
        Canvas.SetTop(needle, Center - 176);
        _secondLayer.Children.Add(needle);
        var dot = new Ellipse { Width = 18, Height = 18, Fill = Second };
        Canvas.SetLeft(dot, Center - 9);
        Canvas.SetTop(dot, Center - 140);
        _secondLayer.Children.Add(dot);
        _second = new RotateTransform { CenterX = Center, CenterY = Center };
        _secondLayer.RenderTransform = _second;
        dial.Children.Add(_secondLayer);

        var hub = new Ellipse { Width = 16, Height = 16, Fill = Ink };
        Canvas.SetLeft(hub, Center - 8);
        Canvas.SetTop(hub, Center - 8);
        dial.Children.Add(hub);

        Content = new Viewbox
        {
            Stretch = Stretch.Uniform,
            Child = dial
        };
    }

    public void SetTime(DateTime time, bool showSeconds)
    {
        var minutes = time.Minute + time.Second / 60.0 + time.Millisecond / 60000.0;
        var hours = (time.Hour % 12) + minutes / 60.0;
        _hour.Angle = hours * 30;
        _minute.Angle = minutes * 6;
        _secondLayer.Visibility = showSeconds ? Visibility.Visible : Visibility.Collapsed;
        if (showSeconds)
        {
            _second.Angle = SecondAngle(time);
        }
    }

    static double SecondAngle(DateTime time)
    {
        var elapsed = time.Second + time.Millisecond / 1000.0;
        const double sweep = 58.5;
        if (elapsed >= sweep)
        {
            return 0;
        }

        return elapsed / sweep * 360.0;
    }

    static Rectangle Index(double angle)
    {
        const double outer = 184;
        const double length = 36;
        const double width = 9;
        var mark = new Rectangle
        {
            Width = width,
            Height = length,
            RadiusX = 1.4,
            RadiusY = 1.4,
            Fill = Ink,
            RenderTransform = new RotateTransform
            {
                Angle = angle,
                CenterX = width / 2,
                CenterY = outer
            }
        };
        Canvas.SetLeft(mark, Center - width / 2);
        Canvas.SetTop(mark, Center - outer);
        return mark;
    }

    static Rectangle Hand(double width, double length, double tip, Brush fill, out RotateTransform rotation)
    {
        var hand = new Rectangle
        {
            Width = width,
            Height = length,
            RadiusX = width / 2,
            RadiusY = width / 2,
            Fill = fill
        };
        Canvas.SetLeft(hand, Center - width / 2);
        Canvas.SetTop(hand, Center - tip);
        rotation = new RotateTransform
        {
            CenterX = width / 2,
            CenterY = tip
        };
        hand.RenderTransform = rotation;
        return hand;
    }
}
