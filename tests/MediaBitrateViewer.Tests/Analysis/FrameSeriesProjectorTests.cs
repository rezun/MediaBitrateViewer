using MediaBitrateViewer.Core.Analysis;
using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Tests.Analysis;

public sealed class FrameSeriesProjectorTests
{
    [Fact]
    public void ZeroDurationFrames_FilledFromTimestampDeltas()
    {
        var frames = new[]
        {
            new FrameRecord { TimestampSeconds = 0,    DurationSeconds = 0, PacketSizeBytes = 5000 },
            new FrameRecord { TimestampSeconds = 0.04, DurationSeconds = 0, PacketSizeBytes = 5000 },
            new FrameRecord { TimestampSeconds = 0.08, DurationSeconds = 0, PacketSizeBytes = 5000 }
        };

        var points = FrameSeriesProjector.ProjectPerFrame(frames);

        Assert.Equal(3, points.Count);
        // Each frame should show 1 Mbps (5000 bytes * 8 / 0.04s / 1e6) via delta fallback
        Assert.All(points, p => Assert.Equal(1.0, p.BitrateMbps, 3));
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        var points = FrameSeriesProjector.ProjectPerFrame(Array.Empty<FrameRecord>());
        Assert.Empty(points);
    }
}
