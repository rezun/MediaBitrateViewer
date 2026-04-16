using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Infrastructure.Cache;

internal sealed record ProbeCacheEntry
{
    public required string CacheVersion { get; init; }
    public required string FilePath { get; init; }
    public required FileFingerprint Fingerprint { get; init; }
    public required string FormatName { get; init; }
    public string? FormatLongName { get; init; }
    public TimeSpan? Duration { get; init; }
    public long? BitRate { get; init; }
    public long? Size { get; init; }
    public required IReadOnlyList<VideoStreamInfo> VideoStreams { get; init; }

    public static ProbeCacheEntry From(ProbedMediaFile probe) => new()
    {
        CacheVersion = AnalysisCache.CacheVersion,
        FilePath = probe.FilePath,
        Fingerprint = probe.Fingerprint,
        FormatName = probe.FormatName,
        FormatLongName = probe.FormatLongName,
        Duration = probe.Duration,
        BitRate = probe.BitRate,
        Size = probe.Size,
        VideoStreams = probe.VideoStreams
    };

    public ProbedMediaFile ToDomain() => new()
    {
        FilePath = FilePath,
        Fingerprint = Fingerprint,
        FormatName = FormatName,
        FormatLongName = FormatLongName,
        Duration = Duration,
        BitRate = BitRate,
        Size = Size,
        VideoStreams = VideoStreams
    };
}

internal sealed record FrameManifest
{
    public required string CacheVersion { get; init; }
    public required FileFingerprint Fingerprint { get; init; }
    public required int StreamIndex { get; init; }
    public required CacheEntryStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required long FrameCount { get; init; }
    public string? ErrorSummary { get; init; }
}
