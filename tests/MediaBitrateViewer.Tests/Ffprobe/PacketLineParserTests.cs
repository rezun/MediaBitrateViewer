using System.Text;
using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Infrastructure.Ffprobe;

namespace MediaBitrateViewer.Tests.Ffprobe;

public sealed class PacketLineParserTests
{
    [Theory]
    [InlineData("0.080000,0.040000,0.040000,1200", 0.08, 0.04)]
    [InlineData("N/A,0.040000,0.040000,1200", 0.04, 0.04)]
    [InlineData("-0.040000,N/A,N/A,1200", -0.04, 0)]
    [InlineData("0.080000,0.040000,0.040000,1200,MPEGTS Stream ID,224\r", 0.08, 0.04)]
    public void PacketTimestamps_PreferPtsAndFallBackToDts(string line, double timestamp, double duration)
    {
        Assert.True(FrameAnalysisService.TryParsePacketLine(Encoding.UTF8.GetBytes(line), out var packet));
        Assert.Equal(timestamp, packet.TimestampSeconds, 6);
        Assert.Equal(duration, packet.DurationSeconds, 6);
        Assert.Equal(1200, packet.PacketSizeBytes);
    }

    [Theory]
    [InlineData("N/A,N/A,0.040000,1200")]
    [InlineData("NaN,Infinity,0.040000,1200")]
    public void PacketWithoutUsableTimestamp_FailsInsteadOfSilentlyDroppingData(string line)
    {
        Assert.Throws<FrameAnalysisException>(() =>
            FrameAnalysisService.TryParsePacketLine(Encoding.UTF8.GetBytes(line), out _));
    }
}
