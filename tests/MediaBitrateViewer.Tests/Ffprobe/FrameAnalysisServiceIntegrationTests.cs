using System.Diagnostics;
using MediaBitrateViewer.Core.Models;
using MediaBitrateViewer.Infrastructure.Ffprobe;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaBitrateViewer.Tests.Ffprobe;

public sealed class FrameAnalysisServiceIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mbv-int-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _videoPath;

    public FrameAnalysisServiceIntegrationTests()
    {
        Directory.CreateDirectory(_dir);
        _videoPath = Path.Combine(_dir, "test.mp4");

        if (!IsToolAvailable("ffmpeg") || !IsToolAvailable("ffprobe"))
            return;

        // Generate a 2-second synthetic video at 25 fps using ffmpeg
        var psi = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("lavfi");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add("testsrc=duration=2:size=320x240:rate=25");
        psi.ArgumentList.Add("-c:v");
        psi.ArgumentList.Add("libx264");
        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add("yuv420p");
        psi.ArgumentList.Add(_videoPath);

        using var proc = Process.Start(psi)!;
        proc.WaitForExit();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static bool IsToolAvailable(string name)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(name, "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p is null) return false;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public async Task ProbeAndAnalyze_RealSyntheticVideo()
    {
        if (!File.Exists(_videoPath))
        {
            // ffmpeg unavailable in this environment; skip silently.
            return;
        }

        var fingerprintService = new MediaBitrateViewer.Infrastructure.Files.FileFingerprintService();
        var fingerprint = await fingerprintService.ComputeAsync(_videoPath, CancellationToken.None);

        var probeService = new FileProbeService(NullLogger<FileProbeService>.Instance);
        var probe = await probeService.ProbeAsync(_videoPath, fingerprint, CancellationToken.None);

        Assert.NotEmpty(probe.VideoStreams);
        var stream = probe.VideoStreams[0];
        Assert.Equal("h264", stream.CodecName);

        var analyzer = new FrameAnalysisService(NullLogger<FrameAnalysisService>.Instance);
        var frames = new List<FrameRecord>();
        await foreach (var frame in analyzer.AnalyzeAsync(_videoPath, stream.Index, CancellationToken.None))
        {
            frames.Add(frame);
        }

        Assert.True(frames.Count >= 25, $"Expected at least 25 packets, got {frames.Count}");
        Assert.All(frames, f => Assert.True(f.DurationSeconds > 0));
        Assert.All(frames, f => Assert.True(f.PacketSizeBytes > 0));
    }
}

