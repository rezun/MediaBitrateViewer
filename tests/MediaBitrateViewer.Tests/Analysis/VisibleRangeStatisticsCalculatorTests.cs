using MediaBitrateViewer.Core.Analysis;
using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Tests.Analysis;

public sealed class VisibleRangeStatisticsCalculatorTests
{
    [Fact]
    public void EmptySeries_ReturnsEmpty()
    {
        var stats = VisibleRangeStatisticsCalculator.Compute(
            Array.Empty<BitratePoint>(), new VisibleTimeRange(0, 1));
        Assert.Equal(0, stats.SampleCount);
    }

    [Fact]
    public void InvalidRange_ReturnsEmpty()
    {
        var pts = new[] { new BitratePoint(0, 1), new BitratePoint(1, 2) };
        var stats = VisibleRangeStatisticsCalculator.Compute(pts, new VisibleTimeRange(2, 1));
        Assert.Equal(0, stats.SampleCount);
    }

    [Fact]
    public void FullRange_ComputesMinMaxAverage()
    {
        var pts = Enumerable.Range(1, 10).Select(i => new BitratePoint(i, i)).ToArray();
        var stats = VisibleRangeStatisticsCalculator.Compute(pts, new VisibleTimeRange(0, 100));
        Assert.Equal(10, stats.SampleCount);
        Assert.Equal(1, stats.MinMbps);
        Assert.Equal(10, stats.MaxMbps);
        Assert.Equal(5.5, stats.AverageMbps);
    }

    [Fact]
    public void Percentile95_ComputedCorrectly()
    {
        // values 1..100 -> 95th percentile interpolated at index 0.95 * 99 = 94.05 -> 95.05
        var pts = Enumerable.Range(1, 100).Select(i => new BitratePoint(i, i)).ToArray();
        var stats = VisibleRangeStatisticsCalculator.Compute(pts, new VisibleTimeRange(0, 200));
        Assert.Equal(100, stats.SampleCount);
        Assert.Equal(95.05, stats.Percentile95Mbps, 2);
    }

    [Fact]
    public void PartialRange_RestrictsToVisibleSubset()
    {
        var pts = Enumerable.Range(0, 10).Select(i => new BitratePoint(i, i + 1.0)).ToArray();
        var stats = VisibleRangeStatisticsCalculator.Compute(pts, new VisibleTimeRange(2, 5));
        // Indices 2..5 inclusive
        Assert.Equal(4, stats.SampleCount);
        Assert.Equal(3, stats.MinMbps);
        Assert.Equal(6, stats.MaxMbps);
    }
}
