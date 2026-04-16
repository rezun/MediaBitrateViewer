using System.Globalization;
using System.Text.Json;
using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Infrastructure.Ffprobe;

public static class ProbeJsonParser
{
    public static ProbedMediaFile Parse(string filePath, FileFingerprint fingerprint, string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var format = root.TryGetProperty("format", out var formatEl) ? formatEl : default;
        var streams = root.TryGetProperty("streams", out var streamsEl) ? streamsEl : default;

        var videoStreams = new List<VideoStreamInfo>();
        if (streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in streams.EnumerateArray())
            {
                if (TryReadString(stream, "codec_type") is "video")
                    videoStreams.Add(ReadVideoStream(stream));
            }
        }

        return new ProbedMediaFile
        {
            FilePath = filePath,
            Fingerprint = fingerprint,
            FormatName = TryReadString(format, "format_name") ?? "unknown",
            FormatLongName = TryReadString(format, "format_long_name"),
            Duration = TryReadDoubleSeconds(format, "duration") is { } dur
                ? TimeSpan.FromSeconds(dur)
                : null,
            BitRate = TryReadLong(format, "bit_rate"),
            Size = TryReadLong(format, "size"),
            VideoStreams = videoStreams
        };
    }

    private static VideoStreamInfo ReadVideoStream(JsonElement stream)
    {
        return new VideoStreamInfo
        {
            Index = TryReadInt(stream, "index") ?? 0,
            CodecName = TryReadString(stream, "codec_name") ?? "unknown",
            CodecLongName = TryReadString(stream, "codec_long_name"),
            Profile = TryReadString(stream, "profile"),
            Width = TryReadInt(stream, "width"),
            Height = TryReadInt(stream, "height"),
            PixelFormat = TryReadString(stream, "pix_fmt"),
            FrameRate = TryReadString(stream, "avg_frame_rate") ?? TryReadString(stream, "r_frame_rate"),
            Duration = TryReadDoubleSeconds(stream, "duration") is { } d ? TimeSpan.FromSeconds(d) : null,
            BitRate = TryReadLong(stream, "bit_rate"),
            NumberOfFrames = TryReadLong(stream, "nb_frames")
        };
    }

    private static string? TryReadString(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.ToString(),
            _ => null
        };
    }

    private static int? TryReadInt(JsonElement el, string name)
    {
        var s = TryReadString(el, name);
        if (s is null) return null;
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static long? TryReadLong(JsonElement el, string name)
    {
        var s = TryReadString(el, name);
        if (s is null) return null;
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static double? TryReadDoubleSeconds(JsonElement el, string name)
    {
        var s = TryReadString(el, name);
        if (s is null) return null;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
