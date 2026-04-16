namespace MediaBitrateViewer.Core.Models;

public sealed record VisibleRangeStatistics
{
    public required VisibleTimeRange Range { get; init; }
    public required int SampleCount { get; init; }
    public required double MinMbps { get; init; }
    public required double MaxMbps { get; init; }
    public required double AverageMbps { get; init; }
    public required double Percentile95Mbps { get; init; }

    public static readonly VisibleRangeStatistics Empty = new()
    {
        Range = default,
        SampleCount = 0,
        MinMbps = 0,
        MaxMbps = 0,
        AverageMbps = 0,
        Percentile95Mbps = 0
    };
}
