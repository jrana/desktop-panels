using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DesktopPanels.Controls;

public sealed class WrapPanel : Panel
{
    public double ItemWidth { get; set; } = 92;
    public double ItemHeight { get; set; } = 104;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? ItemWidth : availableSize.Width;
        double x = 0, y = 0, rowHeight = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(ItemWidth, ItemHeight));
            var childWidth = Math.Max(ItemWidth, child.DesiredSize.Width);
            if (x > 0 && x + childWidth > width)
            {
                x = 0;
                y += rowHeight;
                rowHeight = 0;
            }

            x += childWidth;
            rowHeight = Math.Max(rowHeight, Math.Max(ItemHeight, child.DesiredSize.Height));
        }

        return new Size(width, y + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, rowHeight = 0;
        foreach (var child in Children)
        {
            var childWidth = Math.Max(ItemWidth, child.DesiredSize.Width);
            var childHeight = Math.Max(ItemHeight, child.DesiredSize.Height);
            if (x > 0 && x + childWidth > finalSize.Width)
            {
                x = 0;
                y += rowHeight;
                rowHeight = 0;
            }

            child.Arrange(new Rect(x, y, childWidth, childHeight));
            x += childWidth;
            rowHeight = Math.Max(rowHeight, childHeight);
        }

        return finalSize;
    }
}
