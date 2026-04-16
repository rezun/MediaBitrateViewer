namespace MediaBitrateViewer.Core.Models;

public enum GraphMode
{
    PerSecond = 0,
    PerFrame = 1,
    RollingAverage = 2,
    PeakEnvelope = 3
}

public enum RollingAverageWindow
{
    Ms500 = 500,
    Sec1 = 1000,
    Sec2 = 2000,
    Sec3 = 3000,
    Sec4 = 4000,
    Sec5 = 5000,
    Sec6 = 6000,
    Sec7 = 7000,
    Sec8 = 8000,
    Sec9 = 9000,
    Sec10 = 10000
}

public static class GraphModeExtensions
{
    public static string ToDisplayLabel(this GraphMode mode) =>
        mode switch
        {
            GraphMode.PerSecond => "Per-second",
            GraphMode.PerFrame => "Per-frame",
            GraphMode.RollingAverage => "Rolling average",
            GraphMode.PeakEnvelope => "Peak envelope",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown graph mode")
        };
}

public static class RollingAverageWindowExtensions
{
    public static TimeSpan ToTimeSpan(this RollingAverageWindow window) =>
        TimeSpan.FromMilliseconds((int)window);

    public static double ToSeconds(this RollingAverageWindow window) =>
        (int)window / 1000.0;

    public static string ToDisplayLabel(this RollingAverageWindow window) =>
        window switch
        {
            RollingAverageWindow.Ms500 => "500 ms",
            RollingAverageWindow.Sec1 => "1 s",
            RollingAverageWindow.Sec2 => "2 s",
            RollingAverageWindow.Sec3 => "3 s",
            RollingAverageWindow.Sec4 => "4 s",
            RollingAverageWindow.Sec5 => "5 s",
            RollingAverageWindow.Sec6 => "6 s",
            RollingAverageWindow.Sec7 => "7 s",
            RollingAverageWindow.Sec8 => "8 s",
            RollingAverageWindow.Sec9 => "9 s",
            RollingAverageWindow.Sec10 => "10 s",
            _ => throw new ArgumentOutOfRangeException(nameof(window), window, "Unknown rolling window value")
        };
}
