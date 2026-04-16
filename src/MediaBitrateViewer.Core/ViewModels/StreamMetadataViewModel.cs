using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaBitrateViewer.Core.Formatting;
using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Core.ViewModels;

public sealed partial class StreamMetadataViewModel : ViewModelBase
{
    [ObservableProperty] private VideoStreamInfo? _stream;
    [ObservableProperty] private ProbedMediaFile? _file;

    public bool HasData => Stream is not null;

    public string Index => Stream is { } s ? s.Index.ToString(CultureInfo.InvariantCulture) : "—";
    public string Codec => Stream?.CodecLongName is { } longName && !string.IsNullOrEmpty(longName)
        ? $"{Stream.CodecName} ({longName})"
        : Stream?.CodecName ?? "—";
    public string Profile => Stream?.Profile ?? "—";
    public string Resolution => Stream is { Width: { } w, Height: { } h }
        ? string.Create(CultureInfo.InvariantCulture, $"{w}×{h}")
        : "—";
    public string PixelFormat => Stream?.PixelFormat ?? "—";
    public string FrameRate => FormatFrameRate(Stream?.FrameRate);

    // ffprobe emits frame rates as rational strings (e.g. "24000/1001"). Render as
    // a decimal with up to 3 fractional digits so common cadences read naturally:
    // "23.976 fps", "29.97 fps", "24 fps".
    internal static string FormatFrameRate(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "—";

        double fps;
        var slash = raw.IndexOf('/');
        if (slash < 0)
        {
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out fps))
                return raw;
        }
        else
        {
            var num = raw.AsSpan(0, slash);
            var den = raw.AsSpan(slash + 1);
            if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ||
                !double.TryParse(den, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ||
                d == 0)
            {
                return raw;
            }
            fps = n / d;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{Math.Round(fps, 3):0.###} fps");
    }
    public string Duration => Stream?.Duration is { } d
        ? TimeFormatter.Format(d.TotalSeconds)
        : (File?.Duration is { } fd ? TimeFormatter.Format(fd.TotalSeconds) : "\u2014");
    public string DeclaredBitrate => Stream?.BitRate is { } br
        ? string.Create(CultureInfo.InvariantCulture, $"{br / 1_000_000.0:F2} Mbps")
        : "—";
    public string Container => File?.FormatLongName ?? File?.FormatName ?? "—";

    partial void OnStreamChanged(VideoStreamInfo? value) => RefreshAll();
    partial void OnFileChanged(ProbedMediaFile? value) => RefreshAll();

    private void RefreshAll()
    {
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(Index));
        OnPropertyChanged(nameof(Codec));
        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(Resolution));
        OnPropertyChanged(nameof(PixelFormat));
        OnPropertyChanged(nameof(FrameRate));
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(DeclaredBitrate));
        OnPropertyChanged(nameof(Container));
    }
}
