using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Core.Analysis;

public static class CursorReadoutCalculator
{
    public static CursorReadout AtTime(
        IReadOnlyList<BitratePoint> series,
        double timeSeconds,
        GraphMode mode)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (series.Count == 0) return CursorReadout.Empty(mode);

        var index = FindNearestIndex(series, timeSeconds);
        var point = series[index];
        return new CursorReadout(point.TimeSeconds, point.BitrateMbps, mode, true);
    }

    private static int FindNearestIndex(IReadOnlyList<BitratePoint> series, double t)
    {
        int lo = 0, hi = series.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (series[mid].TimeSeconds < t) lo = mid + 1;
            else hi = mid;
        }

        if (lo > 0)
        {
            var prev = series[lo - 1];
            var curr = series[lo];
            if (Math.Abs(prev.TimeSeconds - t) <= Math.Abs(curr.TimeSeconds - t))
                return lo - 1;
        }
        return lo;
    }
}
