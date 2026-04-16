using MediaBitrateViewer.Core.Models;
using MediaBitrateViewer.Infrastructure.Cache;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaBitrateViewer.Tests.Cache;

public sealed class AnalysisCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mbv-cache-tests-" + Guid.NewGuid().ToString("N"));
    private readonly AnalysisCache _cache;
    private readonly FileFingerprint _fingerprint = new(1024, DateTimeOffset.UnixEpoch, "AAAA", "BBBB");

    public AnalysisCacheTests()
    {
        _cache = new AnalysisCache(_root, NullLogger<AnalysisCache>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private ProbedMediaFile MakeProbe()
    {
        return new ProbedMediaFile
        {
            FilePath = "/tmp/x.mp4",
            Fingerprint = _fingerprint,
            FormatName = "mp4",
            FormatLongName = "MPEG-4",
            Duration = TimeSpan.FromSeconds(120),
            BitRate = 6_000_000,
            Size = 50_000_000,
            VideoStreams = new[]
            {
                new VideoStreamInfo
                {
                    Index = 0,
                    CodecName = "h264",
                    Width = 1280,
                    Height = 720
                }
            }
        };
    }

    [Fact]
    public async Task SaveAndLoadProbe_RoundTrips()
    {
        var probe = MakeProbe();
        await _cache.SaveProbeAsync(probe, CancellationToken.None);
        var loaded = await _cache.TryGetProbeAsync(_fingerprint, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("h264", loaded!.VideoStreams[0].CodecName);
        Assert.Equal(_fingerprint, loaded.Fingerprint);
    }

    [Fact]
    public async Task IncompleteFrameCache_NotReturned()
    {
        await using (var writer = _cache.BeginFrameAnalysis(_fingerprint, 0, TimeProvider.System))
        {
            await writer.AppendAsync(new FrameRecord
            {
                TimestampSeconds = 0,
                DurationSeconds = 0.04,
                PacketSizeBytes = 1000
            }, CancellationToken.None);
            // Do NOT mark complete -> remains in-progress
        }

        var got = await _cache.TryGetCompleteFrameAnalysisAsync(_fingerprint, 0, CancellationToken.None);
        Assert.Null(got);
    }

    [Fact]
    public async Task CompleteFrameCache_Returned()
    {
        var frames = new[]
        {
            new FrameRecord { TimestampSeconds = 0, DurationSeconds = 0.04, PacketSizeBytes = 1000 },
            new FrameRecord { TimestampSeconds = 0.04, DurationSeconds = 0.04, PacketSizeBytes = 500 },
            new FrameRecord { TimestampSeconds = 0.08, DurationSeconds = 0.04, PacketSizeBytes = 250 }
        };

        await using (var writer = _cache.BeginFrameAnalysis(_fingerprint, 0, TimeProvider.System))
        {
            foreach (var f in frames)
                await writer.AppendAsync(f, CancellationToken.None);
            await writer.MarkCompleteAsync(CancellationToken.None);
        }

        var got = await _cache.TryGetCompleteFrameAnalysisAsync(_fingerprint, 0, CancellationToken.None);
        Assert.NotNull(got);
        Assert.Equal(3, got!.Frames.Count);
        Assert.Equal(0.04, got.Frames[1].TimestampSeconds, 5);
        Assert.Equal(250, got.Frames[2].PacketSizeBytes);
    }

    [Fact]
    public async Task CanceledFrameCache_NotReturned()
    {
        await using (var writer = _cache.BeginFrameAnalysis(_fingerprint, 0, TimeProvider.System))
        {
            await writer.AppendAsync(new FrameRecord { DurationSeconds = 0.04, PacketSizeBytes = 100 }, CancellationToken.None);
            await writer.MarkCanceledAsync();
        }
        Assert.Null(await _cache.TryGetCompleteFrameAnalysisAsync(_fingerprint, 0, CancellationToken.None));
    }

    [Fact]
    public async Task ClearFile_RemovesEverything()
    {
        await _cache.SaveProbeAsync(MakeProbe(), CancellationToken.None);
        await _cache.ClearFileAsync(_fingerprint);
        Assert.Null(await _cache.TryGetProbeAsync(_fingerprint, CancellationToken.None));
    }

    [Fact]
    public async Task DifferentFingerprint_NoCacheHit()
    {
        await _cache.SaveProbeAsync(MakeProbe(), CancellationToken.None);
        var differentFp = new FileFingerprint(2048, DateTimeOffset.UnixEpoch, "CCCC", "DDDD");
        Assert.Null(await _cache.TryGetProbeAsync(differentFp, CancellationToken.None));
    }
}
