namespace MediaBitrateViewer.Core.Models;

public readonly record struct FrameRecord
{
    public double TimestampSeconds { get; init; }
    public double DurationSeconds { get; init; }
    public int PacketSizeBytes { get; init; }

    public double BitrateBitsPerSecond =>
        DurationSeconds > 0
            ? PacketSizeBytes * 8.0 / DurationSeconds
            : 0;

    public double BitrateMbps => BitrateBitsPerSecond / 1_000_000.0;
}
