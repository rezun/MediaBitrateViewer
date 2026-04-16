using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Core.Analysis;

/// <summary>
/// Resolves a per-frame effective presentation duration for bitrate calculation.
/// Many codec/container combinations (HEVC in MP4 in particular) omit per-frame
/// durations on a subset of frames, which would otherwise yield bitrate = 0 and
/// create spurious graph dropouts. We fall back to timestamp deltas when the
/// reported duration is zero or missing.
/// </summary>
public static class EffectiveDurationResolver
{
    public static double[] Resolve(IReadOnlyList<FrameRecord> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var n = frames.Count;
        var durations = new double[n];
        if (n == 0) return durations;

        for (var i = 0; i < n; i++)
        {
            var reported = frames[i].DurationSeconds;
            if (reported > 0)
            {
                durations[i] = reported;
                continue;
            }

            // Fallback 1: delta to the next frame's timestamp (frames are in
            // presentation order, so this reflects actual on-screen duration).
            if (i + 1 < n)
            {
                var delta = frames[i + 1].TimestampSeconds - frames[i].TimestampSeconds;
                if (delta > 0)
                {
                    durations[i] = delta;
                    continue;
                }
            }

            // Fallback 2: for the final frame or out-of-order edge cases, reuse
            // the most recent positive duration we have.
            if (i > 0 && durations[i - 1] > 0)
            {
                durations[i] = durations[i - 1];
                continue;
            }

            // No usable duration: leave as zero; caller treats zero as unknown.
            durations[i] = 0;
        }

        return durations;
    }
}
