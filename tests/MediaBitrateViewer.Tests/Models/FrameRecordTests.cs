using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Tests.Models;

public sealed class FrameRecordTests
{
    [Fact]
    public void Bitrate_BasicCalculation()
    {
        var f = new FrameRecord
        {
            TimestampSeconds = 0,
            DurationSeconds = 0.04,
            PacketSizeBytes = 5000
        };
        Assert.Equal(1_000_000, f.BitrateBitsPerSecond, 5);
        Assert.Equal(1.0, f.BitrateMbps, 5);
    }

    [Fact]
    public void Bitrate_ZeroDuration_ReturnsZero()
    {
        var f = new FrameRecord
        {
            TimestampSeconds = 0,
            DurationSeconds = 0,
            PacketSizeBytes = 5000
        };
        Assert.Equal(0, f.BitrateBitsPerSecond);
    }
}
