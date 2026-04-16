namespace MediaBitrateViewer.Core.Models;

public sealed record VideoStreamInfo
{
    public required int Index { get; init; }
    public required string CodecName { get; init; }
    public string? CodecLongName { get; init; }
    public string? Profile { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? PixelFormat { get; init; }
    public string? FrameRate { get; init; }
    public TimeSpan? Duration { get; init; }
    public long? BitRate { get; init; }
    public long? NumberOfFrames { get; init; }

    public string DisplayLabel =>
        Width is not null && Height is not null
            ? $"#{Index} · {CodecName} · {Width}×{Height}"
            : $"#{Index} · {CodecName}";
}
