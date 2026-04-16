namespace MediaBitrateViewer.Core.Models;

public readonly record struct AnalysisProgress(
    long FramesProcessed,
    double LatestTimestampSeconds,
    double? TotalDurationSeconds)
{
    public double? FractionComplete =>
        TotalDurationSeconds is { } total && total > 0
            ? Math.Clamp(LatestTimestampSeconds / total, 0, 1)
            : null;
}
