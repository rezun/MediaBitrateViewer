namespace MediaBitrateViewer.Core.Models;

public readonly record struct CursorReadout(
    double TimeSeconds,
    double BitrateMbps,
    GraphMode Mode,
    bool HasValue)
{
    public static CursorReadout Empty(GraphMode mode) => new(0, 0, mode, false);
}
