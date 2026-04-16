using MediaBitrateViewer.Core.Analysis;
using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Tests.Analysis;

public sealed class EffectiveDurationResolverTests
{
    [Fact]
    public void AllReportedPositive_ReturnedUnchanged()
    {
        var frames = new[]
        {
            new FrameRecord { TimestampSeconds = 0,    DurationSeconds = 0.04 },
            new FrameRecord { TimestampSeconds = 0.04, DurationSeconds = 0.04 },
            new FrameRecord { TimestampSeconds = 0.08, DurationSeconds = 0.04 }
        };

        var r = EffectiveDurationResolver.Resolve(frames);
        Assert.Equal(new[] { 0.04, 0.04, 0.04 }, r);
    }

    [Fact]
    public void ZeroDuration_FilledFromNextTimestampDelta()
    {
        var frames = new[]
        {
            new FrameRecord { TimestampSeconds = 0,    DurationSeconds = 0 },
            new FrameRecord { TimestampSeconds = 0.04, DurationSeconds = 0 },
            new FrameRecord { TimestampSeconds = 0.08, DurationSeconds = 0 }
        };

        var r = EffectiveDurationResolver.Resolve(frames);
        // frame[0] -> delta to frame[1] = 0.04; frame[1] -> delta to frame[2] = 0.04;
        // frame[2] has no next frame, falls back to prior duration = 0.04.
        Assert.Equal(0.04, r[0], 5);
        Assert.Equal(0.04, r[1], 5);
        Assert.Equal(0.04, r[2], 5);
    }

    [Fact]
    public void MixedReportedAndMissing_PrefersReported()
    {
        var frames = new[]
        {
            new FrameRecord { TimestampSeconds = 0,    DurationSeconds = 0.04 },
            new FrameRecord { TimestampSeconds = 0.04, DurationSeconds = 0    },
            new FrameRecord { TimestampSeconds = 0.08, DurationSeconds = 0.05 },
            new FrameRecord { TimestampSeconds = 0.13, DurationSeconds = 0    }
        };

        var r = EffectiveDurationResolver.Resolve(frames);
        Assert.Equal(0.04, r[0], 5);
        Assert.Equal(0.04, r[1], 5); // filled from frame[2] - frame[1]
        Assert.Equal(0.05, r[2], 5);
        Assert.Equal(0.05, r[3], 5); // final frame falls back to previous resolved
    }

    [Fact]
    public void Empty_ReturnsEmpty()
    {
        var r = EffectiveDurationResolver.Resolve(Array.Empty<FrameRecord>());
        Assert.Empty(r);
    }
}
