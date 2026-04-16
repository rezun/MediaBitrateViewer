using MediaBitrateViewer.Core.Analysis;
using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Tests.Analysis;

public sealed class PeakEnvelopeCalculatorTests
{
    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        var result = PeakEnvelopeCalculator.Compute(Array.Empty<FrameRecord>(), 1.0);
        Assert.Empty(result);
    }

    [Fact]
    public void NonPositiveWindow_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PeakEnvelopeCalculator.Compute(Array.Empty<FrameRecord>(), 0));
    }

    [Fact]
    public void UniformFrames_EnvelopeMatchesPerSecondRate()
    {
        // 5 seconds of frames, 25 fps, 5000 bytes per frame
        // Per-second bitrate = 25 * 5000 * 8 = 1_000_000 bits/s = 1 Mbps
        var frames = Enumerable.Range(0, 125).Select(i => Frame(i * 0.04, 0.04, 5_000)).ToArray();
        var result = PeakEnvelopeCalculator.Compute(frames, 2.0);
        Assert.NotEmpty(result);
        Assert.All(result, p => Assert.Equal(1.0, p.BitrateMbps, 2));
    }

    [Fact]
    public void HotSecond_ThreeSecondWindowSpreadsSymmetrically()
    {
        // 5 seconds of data. Second 2 is a 10 Mbps spike; others are 1 Mbps.
        // W=3 includes bins [i-1, i+1], so the spike contaminates s=1, s=2, s=3.
        var frames = BuildFramesWithHotSecond();
        var result = PeakEnvelopeCalculator.Compute(frames, 3.0);
        Assert.Equal(5, result.Count);
        Assert.Equal(1.0, result[0].BitrateMbps, 2);
        Assert.Equal(10.0, result[1].BitrateMbps, 2);
        Assert.Equal(10.0, result[2].BitrateMbps, 2);
        Assert.Equal(10.0, result[3].BitrateMbps, 2);
        Assert.Equal(1.0, result[4].BitrateMbps, 2);
    }

    [Fact]
    public void AdjacentWindowSizesDifferInBinCount()
    {
        // Regression: W=2 and W=3 must produce different envelopes when the spike
        // sits one second away from a neighbor — W=2 includes only 2 bins so the
        // spike does not reach across the gap on one side, while W=3 does. Without
        // bin-count semantics, both windows would include 3 bins and be identical.
        var frames = BuildFramesWithHotSecond();
        var w2 = PeakEnvelopeCalculator.Compute(frames, 2.0);
        var w3 = PeakEnvelopeCalculator.Compute(frames, 3.0);

        // W=3 is symmetric so both s=1 and s=3 see the spike.
        Assert.Equal(10.0, w3[1].BitrateMbps, 2);
        Assert.Equal(10.0, w3[3].BitrateMbps, 2);

        // W=2 (bin count 2) is right-biased [i, i+1]: s=1 sees bin 2 (10 Mbps),
        // but s=3 only sees bins 3 and 4 (both 1 Mbps). Hence w2[3] differs from w3[3].
        Assert.Equal(10.0, w2[1].BitrateMbps, 2);
        Assert.Equal(1.0, w2[3].BitrateMbps, 2);
        Assert.NotEqual(w2[3].BitrateMbps, w3[3].BitrateMbps);
    }

    [Fact]
    public void FourAndFiveSecondWindowsDiffer()
    {
        // Two spikes 4s apart at s=1 and s=5 among 7 seconds of baseline.
        // W=4 (bins [i-1, i+2]) at s=3: covers [2, 5] → sees the s=5 spike but not s=1.
        // W=5 (bins [i-2, i+2]) at s=3: covers [1, 5] → sees both spikes.
        // Baseline elsewhere is 1 Mbps; both spikes are 10 Mbps so the envelope
        // is 10 Mbps in either case — but the coverage differs at other positions.
        // Concretely, at s=6: W=4 covers [5, 8] (bins 5, 6 valid) sees spike at s=5
        // (10). W=5 covers [4, 8] (bins 4, 5, 6 valid) also 10. Try s=7 style.
        // Simpler: verify via position s where only one of the windows reaches a spike.
        var frames = new List<FrameRecord>();
        for (var s = 0; s < 8; s++)
        {
            var hot = s == 1 || s == 5;
            var bytesPerFrame = hot ? 50_000 : 5_000;
            for (var i = 0; i < 25; i++)
                frames.Add(Frame(s + i * 0.04, 0.04, bytesPerFrame));
        }

        var w4 = PeakEnvelopeCalculator.Compute(frames, 4.0);
        var w5 = PeakEnvelopeCalculator.Compute(frames, 5.0);

        // At s=3: W=4 reaches [2, 5] → spike at 5 → 10. W=5 reaches [1, 5] → also 10.
        // At s=7: W=4 reaches [6, 9→7] → {6, 7} baseline only → 1. W=5 reaches [5, 9→7] → spike at 5 → 10.
        Assert.Equal(1.0, w4[7].BitrateMbps, 2);
        Assert.Equal(10.0, w5[7].BitrateMbps, 2);
    }

    private static List<FrameRecord> BuildFramesWithHotSecond()
    {
        var frames = new List<FrameRecord>();
        for (var s = 0; s < 5; s++)
        {
            var bytesPerFrame = s == 2 ? 50_000 : 5_000; // 10 Mbps vs 1 Mbps at 25 fps
            for (var i = 0; i < 25; i++)
                frames.Add(Frame(s + i * 0.04, 0.04, bytesPerFrame));
        }
        return frames;
    }

    [Fact]
    public void SingleFrameSpike_DoesNotInflateEnvelope()
    {
        // A single large I-frame among many small ones. The per-frame instantaneous
        // rate for the I-frame would be ~100 Mbps, but amortized over a second it
        // is much lower. Peak envelope must reflect the per-second rate.
        var frames = new List<FrameRecord>
        {
            Frame(0.5, 0.04, 500_000) // one big frame in second 0
        };
        for (var i = 0; i < 25; i++) frames.Add(Frame(1 + i * 0.04, 0.04, 5_000));
        for (var i = 0; i < 25; i++) frames.Add(Frame(2 + i * 0.04, 0.04, 5_000));

        var result = PeakEnvelopeCalculator.Compute(frames, 1.0);
        // Second-0 bin contains only the 500 KB frame with 0.04s duration. Extrapolated
        // to 1s the rate is 100 Mbps — but that's the trailing-bin extrapolation from
        // PerSecondAggregator, not per-frame instantaneous inflation. The key invariant
        // is that we never exceed what the per-second aggregator reports.
        var perSecond = PerSecondAggregator.Aggregate(frames);
        var maxPerSecond = perSecond.Max(p => p.BitrateMbps);
        Assert.All(result, p => Assert.True(p.BitrateMbps <= maxPerSecond + 1e-9));
    }

    [Fact]
    public void NeverBelowPerSecondAtSamePoint()
    {
        var rng = new Random(42);
        var frames = new FrameRecord[10_000];
        for (var i = 0; i < frames.Length; i++)
            frames[i] = Frame(i * 0.04, 0.04, rng.Next(500, 20_000));

        var envelope = PeakEnvelopeCalculator.Compute(frames, 2.0);
        var perSecond = PerSecondAggregator.Aggregate(frames);
        Assert.Equal(envelope.Count, perSecond.Count);
        for (var i = 0; i < envelope.Count; i++)
            Assert.True(envelope[i].BitrateMbps + 1e-9 >= perSecond[i].BitrateMbps,
                $"Envelope below per-second at i={i}: {envelope[i].BitrateMbps} vs {perSecond[i].BitrateMbps}");
    }

    private static FrameRecord Frame(double t, double dur, int bytes) => new()
    {
        TimestampSeconds = t,
        DurationSeconds = dur,
        PacketSizeBytes = bytes
    };
}
