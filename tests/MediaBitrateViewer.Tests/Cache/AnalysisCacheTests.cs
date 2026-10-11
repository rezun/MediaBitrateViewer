using MediaBitrateViewer.Core.Models;
using MediaBitrateViewer.Infrastructure.Cache;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

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

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(39)]
    [InlineData(60)]
    public async Task CompleteCache_WithWrongFileLength_IsRejected(int length)
    {
        await using (var writer = _cache.BeginFrameAnalysis(_fingerprint, 0, TimeProvider.System))
        {
            await writer.AppendAsync(Packet(1000), CancellationToken.None);
            await writer.AppendAsync(Packet(2000), CancellationToken.None);
            await writer.MarkCompleteAsync(CancellationToken.None);
        }
        var path = Directory.GetFiles(_root, "*.frames.bin", SearchOption.AllDirectories).Single();
        using (var stream = File.OpenWrite(path)) stream.SetLength(length);

        Assert.Null(await _cache.TryGetCompleteFrameAnalysisAsync(_fingerprint, 0, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentIncompleteWriter_DoesNotInvalidateCompletedAnalysis(bool fail)
    {
        await using (var first = _cache.BeginFrameAnalysis(_fingerprint, 0, TimeProvider.System))
        await using (var second = _cache.BeginFrameAnalysis(_fingerprint, 0, TimeProvider.System))
        {
            await first.AppendAsync(Packet(1000), CancellationToken.None);
            await second.AppendAsync(Packet(2000), CancellationToken.None);
            await first.MarkCompleteAsync(CancellationToken.None);
            var during = await _cache.TryGetCompleteFrameAnalysisAsync(_fingerprint, 0, CancellationToken.None);
            Assert.NotNull(during);
            Assert.Equal(1000, Assert.Single(during.Frames).PacketSizeBytes);

            if (fail) await second.MarkFailedAsync("test failure");
            else await second.MarkCanceledAsync();
        }

        var after = await _cache.TryGetCompleteFrameAnalysisAsync(_fingerprint, 0, CancellationToken.None);
        Assert.NotNull(after);
        Assert.Equal(1000, Assert.Single(after.Frames).PacketSizeBytes);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ConcurrentCompleteWriters_PublishConsistentSnapshots()
    {
        await using var first = _cache.BeginFrameAnalysis(_fingerprint, 0, TimeProvider.System);
        await using var second = _cache.BeginFrameAnalysis(_fingerprint, 0, TimeProvider.System);
        await first.AppendAsync(Packet(1000), CancellationToken.None);
        await second.AppendAsync(Packet(2000), CancellationToken.None);
        await second.AppendAsync(Packet(2000), CancellationToken.None);
        await Task.WhenAll(
            first.MarkCompleteAsync(CancellationToken.None).AsTask(),
            second.MarkCompleteAsync(CancellationToken.None).AsTask());

        var result = await _cache.TryGetCompleteFrameAnalysisAsync(_fingerprint, 0, CancellationToken.None);
        Assert.NotNull(result);
        var expectedSize = result.Frames.Count == 1 ? 1000 : 2000;
        Assert.InRange(result.Frames.Count, 1, 2);
        Assert.All(result.Frames, packet => Assert.Equal(expectedSize, packet.PacketSizeBytes));
    }

    [Fact]
    public async Task EmptyAnalysis_CannotBePublishedAsComplete()
    {
        await using var writer = _cache.BeginFrameAnalysis(_fingerprint, 0, TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.MarkCompleteAsync(CancellationToken.None).AsTask());
        Assert.Null(await _cache.TryGetCompleteFrameAnalysisAsync(_fingerprint, 0, CancellationToken.None));
    }

    [Fact]
    public async Task CanceledPublication_PreservesPreviousCompleteCache()
    {
        await using (var first = _cache.BeginFrameAnalysis(_fingerprint, 0, TimeProvider.System))
        {
            await first.AppendAsync(Packet(1000), CancellationToken.None);
            await first.MarkCompleteAsync(CancellationToken.None);
        }
        await using (var replacement = _cache.BeginFrameAnalysis(_fingerprint, 0, TimeProvider.System))
        {
            await replacement.AppendAsync(Packet(2000), CancellationToken.None);
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => replacement.MarkCompleteAsync(cts.Token).AsTask());
            await replacement.MarkCanceledAsync();
        }

        var result = await _cache.TryGetCompleteFrameAnalysisAsync(_fingerprint, 0, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(1000, Assert.Single(result.Frames).PacketSizeBytes);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ConcurrentProbeWrites_DoNotShareTemporaryFiles()
    {
        var first = MakeProbe();
        var second = first with { FilePath = "/tmp/other.mp4" };
        await Task.WhenAll(
            _cache.SaveProbeAsync(first, CancellationToken.None).AsTask(),
            _cache.SaveProbeAsync(second, CancellationToken.None).AsTask());
        Assert.NotNull(await _cache.TryGetProbeAsync(_fingerprint, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ManifestForWrongStream_IsRejected()
    {
        await using (var writer = _cache.BeginFrameAnalysis(_fingerprint, 0, TimeProvider.System))
        {
            await writer.AppendAsync(Packet(1000), CancellationToken.None);
            await writer.MarkCompleteAsync(CancellationToken.None);
        }
        var path = Directory.GetFiles(_root, "*.manifest.json", SearchOption.AllDirectories).Single();
        var manifest = JsonSerializer.Deserialize<FrameManifest>(await File.ReadAllTextAsync(path))
            ?? throw new InvalidDataException("Missing manifest");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest with { StreamIndex = 1 }));

        Assert.Null(await _cache.TryGetCompleteFrameAnalysisAsync(_fingerprint, 0, CancellationToken.None));
    }

    private static FrameRecord Packet(int size) =>
        new() { TimestampSeconds = 0, DurationSeconds = 0.04, PacketSizeBytes = size };
}
