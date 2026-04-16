using MediaBitrateViewer.Core.Analysis;
using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Tests.Analysis;

public sealed class CursorReadoutCalculatorTests
{
    [Fact]
    public void EmptySeries_ReturnsEmptyReadout()
    {
        var r = CursorReadoutCalculator.AtTime(Array.Empty<BitratePoint>(), 5, GraphMode.PerFrame);
        Assert.False(r.HasValue);
    }

    [Fact]
    public void NearestPoint_PickedCorrectly()
    {
        var pts = new[]
        {
            new BitratePoint(0, 1.0),
            new BitratePoint(1, 2.0),
            new BitratePoint(2, 3.0),
            new BitratePoint(3, 4.0)
        };
        var r = CursorReadoutCalculator.AtTime(pts, 1.4, GraphMode.PerFrame);
        Assert.True(r.HasValue);
        Assert.Equal(1, r.TimeSeconds);
        Assert.Equal(2.0, r.BitrateMbps);
    }

    [Fact]
    public void NearestPoint_PicksLaterWhenCloser()
    {
        var pts = new[]
        {
            new BitratePoint(0, 1.0),
            new BitratePoint(1, 2.0),
            new BitratePoint(2, 3.0)
        };
        var r = CursorReadoutCalculator.AtTime(pts, 1.6, GraphMode.RollingAverage);
        Assert.Equal(2, r.TimeSeconds);
        Assert.Equal(3.0, r.BitrateMbps);
        Assert.Equal(GraphMode.RollingAverage, r.Mode);
    }
}
