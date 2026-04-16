using System.Globalization;

namespace MediaBitrateViewer.Core.Formatting;

public static class TimeFormatter
{
    public static string Format(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "\u2014";
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return string.Create(CultureInfo.InvariantCulture,
            $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}");
    }
}
