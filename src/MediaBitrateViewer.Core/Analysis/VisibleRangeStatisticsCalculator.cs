using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Core.Analysis;

public static class VisibleRangeStatisticsCalculator
{
    public static VisibleRangeStatistics Compute(
        IReadOnlyList<BitratePoint> series,
        VisibleTimeRange range)
    {
        ArgumentNullException.ThrowIfNull(series);

        if (!range.IsValid || series.Count == 0)
            return VisibleRangeStatistics.Empty with { Range = range };

        var startIndex = LowerBound(series, range.StartSeconds);
        var endIndex = UpperBound(series, range.EndSeconds);

        var count = endIndex - startIndex;
        if (count <= 0)
            return VisibleRangeStatistics.Empty with { Range = range };

        var values = new double[count];
        var sum = 0.0;
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;

        for (var i = 0; i < count; i++)
        {
            var v = series[startIndex + i].BitrateMbps;
            values[i] = v;
            sum += v;
            if (v < min) min = v;
            if (v > max) max = v;
        }

        Array.Sort(values);
        var p95 = Percentile(values, 0.95);

        return new VisibleRangeStatistics
        {
            Range = range,
            SampleCount = count,
            MinMbps = min,
            MaxMbps = max,
            AverageMbps = sum / count,
            Percentile95Mbps = p95
        };
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 1) return sorted[0];

        var rank = p * (sorted.Length - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        if (lo == hi) return sorted[lo];
        var frac = rank - lo;
        return sorted[lo] + (sorted[hi] - sorted[lo]) * frac;
    }

    private static int LowerBound(IReadOnlyList<BitratePoint> series, double t)
    {
        int lo = 0, hi = series.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (series[mid].TimeSeconds < t) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static int UpperBound(IReadOnlyList<BitratePoint> series, double t)
    {
        int lo = 0, hi = series.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (series[mid].TimeSeconds <= t) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
