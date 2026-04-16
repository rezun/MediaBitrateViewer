using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Core.Analysis;

public static class FrameSeriesProjector
{
    public static IReadOnlyList<BitratePoint> ProjectPerFrame(IReadOnlyList<FrameRecord> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0) return Array.Empty<BitratePoint>();

        var durations = EffectiveDurationResolver.Resolve(frames);
        var output = new BitratePoint[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            var dur = durations[i];
            var mbps = dur > 0 ? f.PacketSizeBytes * 8.0 / dur / 1_000_000.0 : 0;
            output[i] = new BitratePoint(f.TimestampSeconds, mbps);
        }
        return output;
    }
}
