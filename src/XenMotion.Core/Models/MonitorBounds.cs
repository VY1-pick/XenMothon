namespace XenMotion.Core.Models;

/// <summary>
/// Immutable value representation of rectangular monitor dimensions and desktop coordinates.
/// Supports multi-monitor virtual screen setups with negative offsets.
/// </summary>
public readonly record struct MonitorBounds(int X, int Y, int Width, int Height)
{
    public static MonitorBounds Empty => new(0, 0, 0, 0);

    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int px, int py) =>
        px >= X && px < Right && py >= Y && py < Bottom;

    public bool IntersectsWith(MonitorBounds other) =>
        X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;

    public static MonitorBounds Intersect(MonitorBounds a, MonitorBounds b)
    {
        int x1 = Math.Max(a.X, b.X);
        int x2 = Math.Min(a.Right, b.Right);
        int y1 = Math.Max(a.Y, b.Y);
        int y2 = Math.Min(a.Bottom, b.Bottom);

        if (x2 >= x1 && y2 >= y1)
        {
            return new MonitorBounds(x1, y1, x2 - x1, y2 - y1);
        }

        return Empty;
    }
}
