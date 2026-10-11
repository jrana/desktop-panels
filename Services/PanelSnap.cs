using Windows.Graphics;

namespace DesktopPanels.Services;

public static class PanelSnap
{
    public const int GapDip = 10;
    public const int ThresholdDip = 16;
    public const int MinWidth = 180;
    public const int MinHeight = 120;

    [Flags]
    public enum Edge
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8
    }

    public readonly record struct SnapResult(RectInt32 Bounds, Edge Locked);

    public static int ToPixels(int dip, uint dpi)
    {
        var scale = Math.Max(1, dpi / 96.0);
        return (int)Math.Round(dip * scale);
    }

    public static SnapResult Move(RectInt32 proposed, IReadOnlyList<RectInt32> others, int gap, int threshold)
    {
        var slack = threshold + gap;
        var x = proposed.X;
        var y = proposed.Y;
        var foundX = false;
        var foundY = false;
        var bestAbsX = 0;
        var bestAbsY = 0;
        var bestDx = 0;
        var bestDy = 0;
        var right = Right(proposed);
        var bottom = Bottom(proposed);

        foreach (var other in others)
        {
            var otherRight = Right(other);
            var otherBottom = Bottom(other);
            if (RangesNear(proposed.Y, bottom, other.Y, otherBottom, slack))
            {
                Consider(other.X - gap - right, threshold, ref foundX, ref bestAbsX, ref bestDx);
                Consider(otherRight + gap - proposed.X, threshold, ref foundX, ref bestAbsX, ref bestDx);
                Consider(other.X - proposed.X, threshold, ref foundX, ref bestAbsX, ref bestDx);
                Consider(otherRight - right, threshold, ref foundX, ref bestAbsX, ref bestDx);
            }

            if (RangesNear(proposed.X, right, other.X, otherRight, slack))
            {
                Consider(other.Y - gap - bottom, threshold, ref foundY, ref bestAbsY, ref bestDy);
                Consider(otherBottom + gap - proposed.Y, threshold, ref foundY, ref bestAbsY, ref bestDy);
                Consider(other.Y - proposed.Y, threshold, ref foundY, ref bestAbsY, ref bestDy);
                Consider(otherBottom - bottom, threshold, ref foundY, ref bestAbsY, ref bestDy);
            }
        }

        if (foundX)
        {
            x += bestDx;
        }

        if (foundY)
        {
            y += bestDy;
        }

        var snapped = new RectInt32(x, y, proposed.Width, proposed.Height);
        return new SnapResult(snapped, LockedEdges(snapped, others, gap, slack));
    }

    public static SnapResult Resize(RectInt32 proposed, Edge moving, IReadOnlyList<RectInt32> others, int threshold)
    {
        var x = proposed.X;
        var y = proposed.Y;
        var width = proposed.Width;
        var height = proposed.Height;
        var locked = Edge.None;
        var moveLeft = (moving & Edge.Left) != 0;
        var moveRight = (moving & Edge.Right) != 0;
        var moveTop = (moving & Edge.Top) != 0;
        var moveBottom = (moving & Edge.Bottom) != 0;

        if (moveLeft != moveRight)
        {
            var found = false;
            var bestAbs = 0;
            var bestDelta = 0;
            var fixedRight = Right(proposed);
            foreach (var other in others)
            {
                if (!IsStacked(proposed, other, threshold))
                {
                    continue;
                }

                if (moveRight)
                {
                    var toLeft = other.X - fixedRight;
                    var toRight = Right(other) - fixedRight;
                    var match = other.Width - proposed.Width;
                    ConsiderFit(toLeft, proposed.Width + toLeft, MinWidth, threshold, ref found, ref bestAbs, ref bestDelta);
                    ConsiderFit(toRight, proposed.Width + toRight, MinWidth, threshold, ref found, ref bestAbs, ref bestDelta);
                    ConsiderFit(match, other.Width, MinWidth, threshold, ref found, ref bestAbs, ref bestDelta);
                }
                else
                {
                    ConsiderFit(other.X - proposed.X, fixedRight - other.X, MinWidth, threshold, ref found, ref bestAbs, ref bestDelta);
                    ConsiderFit(Right(other) - proposed.X, fixedRight - Right(other), MinWidth, threshold, ref found, ref bestAbs, ref bestDelta);
                    ConsiderFit(proposed.Width - other.Width, other.Width, MinWidth, threshold, ref found, ref bestAbs, ref bestDelta);
                }
            }

            if (found)
            {
                if (moveRight)
                {
                    width = proposed.Width + bestDelta;
                }
                else
                {
                    x = proposed.X + bestDelta;
                    width = fixedRight - x;
                }

                locked |= moveRight ? Edge.Right : Edge.Left;
            }
        }

        if (moveTop != moveBottom)
        {
            var found = false;
            var bestAbs = 0;
            var bestDelta = 0;
            var fixedBottom = Bottom(proposed);
            foreach (var other in others)
            {
                if (!IsBeside(proposed, other, threshold))
                {
                    continue;
                }

                if (moveBottom)
                {
                    var toTop = other.Y - fixedBottom;
                    var toBottom = Bottom(other) - fixedBottom;
                    var match = other.Height - proposed.Height;
                    ConsiderFit(toTop, proposed.Height + toTop, MinHeight, threshold, ref found, ref bestAbs, ref bestDelta);
                    ConsiderFit(toBottom, proposed.Height + toBottom, MinHeight, threshold, ref found, ref bestAbs, ref bestDelta);
                    ConsiderFit(match, other.Height, MinHeight, threshold, ref found, ref bestAbs, ref bestDelta);
                }
                else
                {
                    ConsiderFit(other.Y - proposed.Y, fixedBottom - other.Y, MinHeight, threshold, ref found, ref bestAbs, ref bestDelta);
                    ConsiderFit(Bottom(other) - proposed.Y, fixedBottom - Bottom(other), MinHeight, threshold, ref found, ref bestAbs, ref bestDelta);
                    ConsiderFit(proposed.Height - other.Height, other.Height, MinHeight, threshold, ref found, ref bestAbs, ref bestDelta);
                }
            }

            if (found)
            {
                if (moveBottom)
                {
                    height = proposed.Height + bestDelta;
                }
                else
                {
                    y = proposed.Y + bestDelta;
                    height = fixedBottom - y;
                }

                locked |= moveBottom ? Edge.Bottom : Edge.Top;
            }
        }

        return new SnapResult(new RectInt32(x, y, width, height), locked);
    }

    static void Consider(int delta, int threshold, ref bool found, ref int bestAbs, ref int bestDelta)
    {
        var abs = Math.Abs(delta);
        if (abs > threshold || (found && abs >= bestAbs))
        {
            return;
        }

        found = true;
        bestAbs = abs;
        bestDelta = delta;
    }

    static void ConsiderFit(int delta, int size, int minimum, int threshold, ref bool found, ref int bestAbs, ref int bestDelta)
    {
        if (size < minimum)
        {
            return;
        }

        Consider(delta, threshold, ref found, ref bestAbs, ref bestDelta);
    }

    static bool IsStacked(RectInt32 panel, RectInt32 other, int slack)
    {
        if (!RangesNear(panel.X, Right(panel), other.X, Right(other), slack))
        {
            return false;
        }

        var verticalOverlap = RangesOverlap(panel.Y, Bottom(panel), other.Y, Bottom(other));
        var horizontalOverlap = RangesOverlap(panel.X, Right(panel), other.X, Right(other));
        return !(verticalOverlap && !horizontalOverlap);
    }

    static bool IsBeside(RectInt32 panel, RectInt32 other, int slack)
    {
        if (!RangesNear(panel.Y, Bottom(panel), other.Y, Bottom(other), slack))
        {
            return false;
        }

        var horizontalOverlap = RangesOverlap(panel.X, Right(panel), other.X, Right(other));
        var verticalOverlap = RangesOverlap(panel.Y, Bottom(panel), other.Y, Bottom(other));
        return !(horizontalOverlap && !verticalOverlap);
    }

    static Edge LockedEdges(RectInt32 rect, IReadOnlyList<RectInt32> others, int gap, int slack)
    {
        var edges = Edge.None;
        var right = Right(rect);
        var bottom = Bottom(rect);
        foreach (var other in others)
        {
            var otherRight = Right(other);
            var otherBottom = Bottom(other);
            if (RangesNear(rect.Y, bottom, other.Y, otherBottom, slack))
            {
                if (rect.X == otherRight + gap || rect.X == other.X)
                {
                    edges |= Edge.Left;
                }

                if (right == other.X - gap || right == otherRight)
                {
                    edges |= Edge.Right;
                }
            }

            if (RangesNear(rect.X, right, other.X, otherRight, slack))
            {
                if (rect.Y == otherBottom + gap || rect.Y == other.Y)
                {
                    edges |= Edge.Top;
                }

                if (bottom == other.Y - gap || bottom == otherBottom)
                {
                    edges |= Edge.Bottom;
                }
            }
        }

        return edges;
    }

    static bool RangesNear(int startA, int endA, int startB, int endB, int slack) =>
        startA <= endB + slack && startB <= endA + slack;

    static bool RangesOverlap(int startA, int endA, int startB, int endB) =>
        startA < endB && startB < endA;

    static int Right(RectInt32 rect) => rect.X + rect.Width;

    static int Bottom(RectInt32 rect) => rect.Y + rect.Height;
}
