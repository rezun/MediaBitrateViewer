using MediaBitrateViewer.Core.Analysis;
using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Tests.Analysis;

public sealed class PerSecondAggregatorTests
{
    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        var points = PerSecondAggregator.Aggregate(Array.Empty<FrameRecord>());
        Assert.Empty(points);
    }

    // Use a fixed 30fps frame rate across tests so bin coverage scales with frame count:
    // 30 frames = full second, 15 frames = half second, etc.
    private const int Fps = 30;
    private const double FrameDuration = 1.0 / Fps;

    [Fact]
    public void FullSecondBins_RateEqualsSummedBitsPerSecond()
    {
        // Bin 0 total bytes = 125_000 → 1 Mbps.
        // Bin 1 total bytes = 250_000 → 2 Mbps.
        var frames = BuildFrames(
            (binStart: 0, frameCount: Fps, bytesPerFrame: 125_000 / Fps),
            (binStart: 1, frameCount: Fps, bytesPerFrame: 250_000 / Fps));

        var points = PerSecondAggregator.Aggregate(frames);

        Assert.Equal(2, points.Count);
        Assert.Equal(0, points[0].TimeSeconds);
        Assert.Equal(1.0, points[0].BitrateMbps, 3);
        Assert.Equal(1, points[1].TimeSeconds);
        Assert.Equal(2.0, points[1].BitrateMbps, 3);
    }

    [Fact]
    public void PartialTrailingBin_ScalesToFullSecondEquivalent()
    {
        // Bin 0: full 30 frames of content → 1 Mbps.
        // Bin 1: only 15 frames at same per-frame bitrate (stream ended mid-second) →
        // must also report 1 Mbps because scaling accounts for the partial coverage.
        var perFrameBytes = 125_000 / Fps;
        var frames = BuildFrames(
            (binStart: 0, frameCount: Fps,     bytesPerFrame: perFrameBytes),
            (binStart: 1, frameCount: Fps / 2, bytesPerFrame: perFrameBytes));

        var points = PerSecondAggregator.Aggregate(frames);

        Assert.Equal(2, points.Count);
        Assert.Equal(1.0, points[0].BitrateMbps, 3);
        Assert.Equal(1.0, points[1].BitrateMbps, 3);
    }

    [Fact]
    public void ExcludeTrailingBin_DropsHighestSecondForProgressiveRendering()
    {
        var frames = BuildFrames(
            (binStart: 0, frameCount: Fps, bytesPerFrame: 1000),
            (binStart: 1, frameCount: Fps, bytesPerFrame: 1000),
            (binStart: 2, frameCount: 5,   bytesPerFrame: 1000));

        var points = PerSecondAggregator.Aggregate(frames, excludeTrailingBin: true);

        Assert.Equal(2, points.Count);
        Assert.Equal(0, points[0].TimeSeconds);
        Assert.Equal(1, points[1].TimeSeconds);
    }

    [Fact]
    public void GapsBetweenFrames_EmitZeroBins()
    {
        var frames = BuildFrames(
            (binStart: 0, frameCount: Fps, bytesPerFrame: 125_000 / Fps),
            (binStart: 3, frameCount: Fps, bytesPerFrame: 125_000 / Fps));

        var points = PerSecondAggregator.Aggregate(frames);

        Assert.Equal(4, points.Count);
        Assert.Equal(1.0, points[0].BitrateMbps, 3);
        Assert.Equal(0.0, points[1].BitrateMbps);
        Assert.Equal(0.0, points[2].BitrateMbps);
        Assert.Equal(1.0, points[3].BitrateMbps, 3);
    }

    private static FrameRecord[] BuildFrames(params (int binStart, int frameCount, int bytesPerFrame)[] groups)
    {
        var list = new List<FrameRecord>();
        foreach (var (binStart, frameCount, bytesPerFrame) in groups)
        {
            for (var i = 0; i < frameCount; i++)
            {
                list.Add(new FrameRecord
                {
                    TimestampSeconds = binStart + i * FrameDuration,
                    DurationSeconds = FrameDuration,
                    PacketSizeBytes = bytesPerFrame
                });
            }
        }
        return list.ToArray();
    }
}
