using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Core.Analysis;

public static class PerSecondAggregator
{
    /// <summary>
    /// Bucket frames into fixed 1-second bins keyed by floor(timestamp) and emit one
    /// point per second whose bitrate is the frames' total bytes divided by their
    /// total effective duration. For full bins this yields ~bytes*8 (the sum of bits
    /// delivered in that second); for a partial trailing bin at end-of-stream the
    /// denominator is below 1 s, so the rate scales up to a full-second equivalent
    /// instead of dropping into a valley. Gap bins (no frames) emit 0.
    /// </summary>
    /// <param name="excludeTrailingBin">
    /// When true, omits the highest-numbered bin from the output. Callers rendering
    /// progressively should set this to true because the trailing bin is still being
    /// filled — its rate would jitter as each new frame arrives.
    /// </param>
    public static IReadOnlyList<BitratePoint> Aggregate(
        IReadOnlyList<FrameRecord> frames,
        bool excludeTrailingBin = false)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0) return Array.Empty<BitratePoint>();

        var firstSecond = int.MaxValue;
        var lastSecond = int.MinValue;
        for (var i = 0; i < frames.Count; i++)
        {
            var ts = frames[i].TimestampSeconds;
            if (!double.IsFinite(ts) || ts < 0) continue;
            var second = (int)Math.Floor(ts);
            if (second < firstSecond) firstSecond = second;
            if (second > lastSecond) lastSecond = second;
        }

        if (lastSecond < firstSecond) return Array.Empty<BitratePoint>();

        var effectiveLast = excludeTrailingBin ? lastSecond - 1 : lastSecond;
        if (effectiveLast < firstSecond) return Array.Empty<BitratePoint>();

        var durations = EffectiveDurationResolver.Resolve(frames);
        var binCount = lastSecond - firstSecond + 1;
        var byteBins = new long[binCount];
        var durationBins = new double[binCount];

        for (var i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            if (!double.IsFinite(f.TimestampSeconds) || f.TimestampSeconds < 0) continue;
            var index = (int)Math.Floor(f.TimestampSeconds) - firstSecond;
            byteBins[index] += f.PacketSizeBytes;
            durationBins[index] += durations[i];
        }

        // Valid frames have TimestampSeconds >= 0, so firstSecond >= 0 and output starts at 0.
        // Seconds below firstSecond emit zero so the graph's x-axis begins at t=0.
        var length = effectiveLast + 1;
        var output = new BitratePoint[length];
        for (var s = 0; s < firstSecond; s++)
        {
            output[s] = new BitratePoint(s, 0);
        }
        var emitLast = Math.Min(effectiveLast, lastSecond);
        for (var s = firstSecond; s <= emitLast; s++)
        {
            var bytes = byteBins[s - firstSecond];
            var duration = durationBins[s - firstSecond];
            var mbps = duration > 0 ? bytes * 8.0 / duration / 1_000_000.0 : 0;
            output[s] = new BitratePoint(s, mbps);
        }
        return output;
    }
}
