using MediaBitrateViewer.Core.Models;
using MediaBitrateViewer.Infrastructure.Ffprobe;

namespace MediaBitrateViewer.Tests.Ffprobe;

public sealed class ProbeJsonParserTests
{
    private static readonly FileFingerprint TestFingerprint =
        new(1024, DateTimeOffset.UnixEpoch, "AA", "BB");

    [Fact]
    public void Parse_SingleVideoStream()
    {
        const string json = """
        {
          "streams": [
            {
              "index": 0,
              "codec_name": "h264",
              "codec_long_name": "H.264",
              "codec_type": "video",
              "profile": "High",
              "width": 1920,
              "height": 1080,
              "pix_fmt": "yuv420p",
              "avg_frame_rate": "30000/1001",
              "duration": "60.0",
              "bit_rate": "5000000",
              "nb_frames": "1798"
            }
          ],
          "format": {
            "format_name": "mp4",
            "format_long_name": "MP4",
            "duration": "60.0",
            "bit_rate": "6000000",
            "size": "45000000"
          }
        }
        """;

        var probe = ProbeJsonParser.Parse("/tmp/x.mp4", TestFingerprint, json);

        Assert.Equal("mp4", probe.FormatName);
        Assert.Single(probe.VideoStreams);
        var s = probe.VideoStreams[0];
        Assert.Equal(0, s.Index);
        Assert.Equal("h264", s.CodecName);
        Assert.Equal(1920, s.Width);
        Assert.Equal(1080, s.Height);
        Assert.Equal(5_000_000, s.BitRate);
        Assert.Equal(TimeSpan.FromSeconds(60.0), s.Duration);
    }

    [Fact]
    public void Parse_MultipleStreams_OnlyVideoCounted()
    {
        const string json = """
        {
          "streams": [
            { "index": 0, "codec_name": "aac", "codec_type": "audio" },
            { "index": 1, "codec_name": "h264", "codec_type": "video", "width": 640, "height": 360 },
            { "index": 2, "codec_name": "hevc", "codec_type": "video", "width": 1280, "height": 720 },
            { "index": 3, "codec_name": "mov_text", "codec_type": "subtitle" }
          ],
          "format": { "format_name": "mp4" }
        }
        """;

        var probe = ProbeJsonParser.Parse("/tmp/x.mp4", TestFingerprint, json);

        Assert.Equal(2, probe.VideoStreams.Count);
        Assert.Equal(1, probe.VideoStreams[0].Index);
        Assert.Equal(2, probe.VideoStreams[1].Index);
    }

    [Fact]
    public void Parse_NoVideoStreams_ReturnsEmptyVideoList()
    {
        const string json = """
        {
          "streams": [
            { "index": 0, "codec_name": "aac", "codec_type": "audio" }
          ],
          "format": { "format_name": "mp4" }
        }
        """;

        var probe = ProbeJsonParser.Parse("/tmp/x.mp4", TestFingerprint, json);
        Assert.Empty(probe.VideoStreams);
    }

    [Fact]
    public void Parse_PreservesFingerprint()
    {
        const string json = """{"streams":[],"format":{"format_name":"webm"}}""";
        var probe = ProbeJsonParser.Parse("/tmp/y.webm", TestFingerprint, json);
        Assert.Equal(TestFingerprint, probe.Fingerprint);
    }
}
