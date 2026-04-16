using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Core.Analysis;

public static class RollingAverageCalculator
{
    /// <summary>
    /// Compute centered-window rolling average bitrate over the supplied frame records.
    /// Each output point's bitrate is the bytes-weighted average across all frames whose
    /// presentation interval falls within +/- (windowSeconds / 2) of the frame's timestamp.
    /// Effective per-frame durations are resolved via <see cref="EffectiveDurationResolver"/>
    /// so that frames without reported duration still contribute correctly.
    /// </summary>
    public static IReadOnlyList<BitratePoint> Compute(
        IReadOnlyList<FrameRecord> frames,
        double windowSeconds)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (windowSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowSeconds), windowSeconds, "Window must be positive");

        if (frames.Count == 0)
            return Array.Empty<BitratePoint>();

        var durations = EffectiveDurationResolver.Resolve(frames);
        var half = windowSeconds / 2.0;
        var output = new BitratePoint[frames.Count];

        var windowStart = 0;
        var windowEnd = 0;
        long bytesInWindow = 0;
        double durationInWindow = 0;

        for (var i = 0; i < frames.Count; i++)
        {
            var center = frames[i].TimestampSeconds;
            var lower = center - half;
            var upper = center + half;

            while (windowStart < frames.Count && frames[windowStart].TimestampSeconds < lower)
            {
                bytesInWindow -= frames[windowStart].PacketSizeBytes;
                durationInWindow -= durations[windowStart];
                windowStart++;
            }

            while (windowEnd < frames.Count && frames[windowEnd].TimestampSeconds <= upper)
            {
                bytesInWindow += frames[windowEnd].PacketSizeBytes;
                durationInWindow += durations[windowEnd];
                windowEnd++;
            }

            var bitsPerSecond = durationInWindow > 0
                ? bytesInWindow * 8.0 / durationInWindow
                : 0;

            output[i] = new BitratePoint(center, bitsPerSecond / 1_000_000.0);
        }

        return output;
    }
}
