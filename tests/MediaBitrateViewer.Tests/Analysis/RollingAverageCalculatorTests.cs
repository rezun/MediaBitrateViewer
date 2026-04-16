using MediaBitrateViewer.Core.Analysis;
using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Tests.Analysis;

public sealed class RollingAverageCalculatorTests
{
    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        var result = RollingAverageCalculator.Compute(Array.Empty<FrameRecord>(), 1.0);
        Assert.Empty(result);
    }

    [Fact]
    public void SingleFrame_ReturnsSinglePoint()
    {
        var frame = new FrameRecord
        {
            TimestampSeconds = 0,
            DurationSeconds = 0.04,
            PacketSizeBytes = 5000
        };
        var result = RollingAverageCalculator.Compute(new[] { frame }, 1.0);
        Assert.Single(result);
        Assert.Equal(0, result[0].TimeSeconds);
        // 5000 bytes * 8 / 0.04s = 1_000_000 bits/s -> 1 Mbps
        Assert.Equal(1.0, result[0].BitrateMbps, 5);
    }

    [Fact]
    public void UniformFrames_AverageMatchesIndividualBitrate()
    {
        var frames = Enumerable.Range(0, 100).Select(i => new FrameRecord
        {
            TimestampSeconds = i * 0.04,
            DurationSeconds = 0.04,
            PacketSizeBytes = 5000
        }).ToArray();

        var result = RollingAverageCalculator.Compute(frames, 1.0);
        Assert.Equal(100, result.Count);
        Assert.All(result, p => Assert.Equal(1.0, p.BitrateMbps, 3));
    }

    [Fact]
    public void NonPositiveWindow_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RollingAverageCalculator.Compute(Array.Empty<FrameRecord>(), 0));
    }
}
