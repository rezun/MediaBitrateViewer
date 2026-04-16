namespace MediaBitrateViewer.Core.Models;

public sealed record WindowPlacement
{
    public int X { get; init; }
    public int Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public WindowStateKind State { get; init; } = WindowStateKind.Normal;
}

public enum WindowStateKind
{
    Normal = 0,
    Maximized = 1
}
