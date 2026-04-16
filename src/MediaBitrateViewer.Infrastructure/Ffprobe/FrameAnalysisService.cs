using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.Models;
using Microsoft.Extensions.Logging;

namespace MediaBitrateViewer.Infrastructure.Ffprobe;

public sealed class FrameAnalysisService : IFrameAnalysisService
{
    private static ReadOnlySpan<byte> NotAvailable => "N/A"u8;

    private readonly ILogger<FrameAnalysisService> _logger;

    public FrameAnalysisService(ILogger<FrameAnalysisService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async IAsyncEnumerable<FrameRecord> AnalyzeAsync(
        string filePath,
        int videoStreamIndex,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        // Packets carry everything we need for bitrate visualization (pts, duration, size)
        // and are read straight from the container without going through the decoder
        // pipeline — typically 10-50x faster than -show_frames.
        var psi = new ProcessStartInfo("ffprobe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-select_streams");
        psi.ArgumentList.Add(videoStreamIndex.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("-show_entries");
        psi.ArgumentList.Add("packet=pts_time,duration_time,size");
        psi.ArgumentList.Add("-of");
        psi.ArgumentList.Add("csv=p=0");
        psi.ArgumentList.Add(filePath);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffprobe process");

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            // Read raw bytes from stdout via a pipe and parse line-by-line in UTF-8 with
            // zero managed-string allocations per packet (Utf8Parser on byte spans).
            var reader = PipeReader.Create(process.StandardOutput.BaseStream);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = result.Buffer;

                while (TryReadLine(ref buffer, out var line))
                {
                    if (TryParsePacketLine(in line, out var record))
                        yield return record;
                }

                if (result.IsCompleted)
                {
                    if (!buffer.IsEmpty && TryParsePacketLine(in buffer, out var record))
                        yield return record;
                    reader.AdvanceTo(buffer.End);
                    break;
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
            }

            await reader.CompleteAsync().ConfigureAwait(false);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                _logger.LogError("ffprobe -show_packets exited with {ExitCode}: {Stderr}", process.ExitCode, stderr);
                throw new FrameAnalysisException($"ffprobe failed with exit code {process.ExitCode}: {stderr.Trim()}");
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* already exited */ }
            }
        }
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        var position = buffer.PositionOf((byte)'\n');
        if (position is null)
        {
            line = default;
            return false;
        }

        line = buffer.Slice(0, position.Value);
        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
        return true;
    }

    private static bool TryParsePacketLine(in ReadOnlySequence<byte> line, out FrameRecord frame)
    {
        if (line.IsSingleSegment)
            return TryParsePacketLine(line.FirstSpan, out frame);

        // Rare: line straddles pipe segments. Stackalloc avoids a heap allocation.
        var len = (int)line.Length;
        if (len > 256)
        {
            // Defensive — packet CSV lines are well under this in practice.
            frame = default;
            return false;
        }
        Span<byte> tmp = stackalloc byte[len];
        line.CopyTo(tmp);
        return TryParsePacketLine(tmp, out frame);
    }

    private static bool TryParsePacketLine(ReadOnlySpan<byte> line, out FrameRecord frame)
    {
        frame = default;

        // CRLF tolerance — ffprobe writes LF on Unix but be defensive.
        if (!line.IsEmpty && line[^1] == (byte)'\r')
            line = line[..^1];
        if (line.IsEmpty) return false;

        // Expected: pts_time,duration_time,size
        var c1 = line.IndexOf((byte)',');
        if (c1 < 0) return false;
        var rest = line[(c1 + 1)..];
        var c2 = rest.IndexOf((byte)',');
        if (c2 < 0) return false;

        var ptsSpan = line[..c1];
        var durSpan = rest[..c2];
        var sizeSpan = rest[(c2 + 1)..];

        if (!Utf8Parser.TryParse(ptsSpan, out double pts, out _))
            return false;

        // duration_time may be N/A on some codec/container combinations; let
        // EffectiveDurationResolver backfill from timestamp deltas downstream.
        double dur = 0;
        if (!durSpan.SequenceEqual(NotAvailable))
            Utf8Parser.TryParse(durSpan, out dur, out _);

        if (!Utf8Parser.TryParse(sizeSpan, out int size, out _))
            return false;

        frame = new FrameRecord
        {
            TimestampSeconds = pts,
            DurationSeconds = dur,
            PacketSizeBytes = size
        };
        return true;
    }
}
