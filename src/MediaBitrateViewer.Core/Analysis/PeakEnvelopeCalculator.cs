using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Core.Analysis;

public static class PeakEnvelopeCalculator
{
    /// <summary>
    /// Compute a centered-window peak envelope over the per-second bitrate series.
    /// Each output point's value is the maximum per-second bitrate (Mbps) across
    /// all 1-second bins whose center falls within +/- (windowSeconds / 2) of the
    /// output bin. Operating on per-second rates (not per-frame rates) avoids
    /// inflating single-frame I-frame spikes into unrealistic instantaneous peaks;
    /// the result represents a realistic short-duration sustained bitrate ceiling,
    /// matching how plotbitrate's "downscale" option is interpreted.
    /// </summary>
    public static IReadOnlyList<BitratePoint> Compute(
        IReadOnlyList<FrameRecord> frames,
        double windowSeconds,
        bool excludeTrailingBin = false)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (windowSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowSeconds), windowSeconds, "Window must be positive");

        if (frames.Count == 0)
            return Array.Empty<BitratePoint>();

        var perSecond = PerSecondAggregator.Aggregate(frames, excludeTrailingBin);
        if (perSecond.Count == 0) return Array.Empty<BitratePoint>();

        // Bin-count semantics: at each output bin, take the max over W consecutive
        // 1-second bins centered on it. Time-range bounds would round to the same
        // bin count for every even/odd pair (e.g. W=2 and W=3 both include 3 bins
        // when both endpoints are inclusive at integer timestamps), so users couldn't
        // distinguish adjacent window sizes. Even W produces a slight right-bias
        // (one more bin to the right than left), which is acceptable visual cost.
        var windowBins = Math.Max(1, (int)Math.Round(windowSeconds));
        var leftRadius = (windowBins - 1) / 2;
        var rightRadius = windowBins / 2;

        var output = new BitratePoint[perSecond.Count];

        // Monotonic deque of indexes whose per-second bitrates are candidates for
        // the current window's maximum. Front is always the current max. Amortized
        // O(1) per bin, O(n) total.
        var deque = new int[perSecond.Count];
        var head = 0;
        var tail = 0;

        for (var i = 0; i < perSecond.Count; i++)
        {
            var addIndex = i + rightRadius;
            if (addIndex >= perSecond.Count) addIndex = perSecond.Count - 1;
            // Only actually admit new indexes we haven't seen yet.
            var lastAdmitted = tail > 0 ? deque[tail - 1] : -1;
            for (var j = lastAdmitted + 1; j <= addIndex; j++)
            {
                var v = perSecond[j].BitrateMbps;
                while (head < tail && perSecond[deque[tail - 1]].BitrateMbps <= v)
                    tail--;
                deque[tail++] = j;
            }

            var dropBefore = i - leftRadius;
            while (head < tail && deque[head] < dropBefore)
                head++;

            var maxMbps = head < tail ? perSecond[deque[head]].BitrateMbps : 0;
            output[i] = new BitratePoint(perSecond[i].TimeSeconds, maxMbps);
        }

        return output;
    }
}
