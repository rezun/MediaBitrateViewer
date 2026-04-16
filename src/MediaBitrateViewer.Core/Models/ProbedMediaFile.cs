namespace MediaBitrateViewer.Core.Models;

public sealed record ProbedMediaFile
{
    public required string FilePath { get; init; }
    public required FileFingerprint Fingerprint { get; init; }
    public required string FormatName { get; init; }
    public string? FormatLongName { get; init; }
    public TimeSpan? Duration { get; init; }
    public long? BitRate { get; init; }
    public long? Size { get; init; }
    public required IReadOnlyList<VideoStreamInfo> VideoStreams { get; init; }
}
