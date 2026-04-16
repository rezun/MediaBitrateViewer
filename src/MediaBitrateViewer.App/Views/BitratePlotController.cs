using Avalonia;
using Avalonia.Styling;
using MediaBitrateViewer.Core.Analysis;
using MediaBitrateViewer.Core.Models;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;

namespace MediaBitrateViewer.App.Views;

/// <summary>
/// Owns ScottPlot interaction for the bitrate graph: series buffers, axis rules,
/// theming, cursor line, and viewport application. Keeps the view code-behind
/// focused on DataContext wiring and input-event forwarding.
/// </summary>
public sealed class BitratePlotController
{
    private const int InitialCapacity = 4096;

    private readonly AvaPlot _plot;
    private Scatter? _scatter;
    private VerticalLine? _cursorLine;
    private ScottPlot.AxisRules.LockedVertical? _yAxisRule;
    private ScottPlot.AxisRules.MaximumBoundary? _xBoundaryRule;
    private double[] _xs = Array.Empty<double>();
    private double[] _ys = Array.Empty<double>();
    private int _seriesCount;
    private double _lastAppliedYTop;
    private double _lastAppliedXRight;
    private double _yZoomLevel = 1.0;

    public BitratePlotController(AvaPlot plot)
    {
        ArgumentNullException.ThrowIfNull(plot);
        _plot = plot;
    }

    public int SeriesCount => _seriesCount;

    public void ApplyChrome(ThemeVariant variant)
    {
        var bg = ResolveBrushColor("GraphBackgroundBrush", variant);
        var grid = ResolveBrushColor("GraphGridBrush", variant);
        var axis = ResolveBrushColor("GraphAxisBrush", variant);
        var fg = ResolveBrushColor("TextPrimaryBrush", variant);

        _plot.Plot.FigureBackground.Color = bg;
        _plot.Plot.DataBackground.Color = bg;
        _plot.Plot.Axes.Color(axis);
        _plot.Plot.Grid.MajorLineColor = grid;
        _plot.Plot.Grid.MinorLineColor = grid;

        _plot.Plot.Axes.Bottom.Label.Text = "Time";
        _plot.Plot.Axes.Left.Label.Text = "Bitrate (Mbps)";
        _plot.Plot.Axes.Bottom.Label.ForeColor = fg;
        _plot.Plot.Axes.Left.Label.ForeColor = fg;
        _plot.Plot.Axes.Bottom.TickLabelStyle.ForeColor = axis;
        _plot.Plot.Axes.Left.TickLabelStyle.ForeColor = axis;

        if (_plot.Plot.Axes.Bottom.TickGenerator is ScottPlot.TickGenerators.NumericAutomatic xGen)
        {
            xGen.LabelFormatter = FormatSecondsAsClock;
        }

        ApplySeriesColors(variant);
    }

    public void ApplySeriesColors(ThemeVariant variant)
    {
        if (_scatter is not null)
        {
            _scatter.Color = ResolveBrushColor("GraphSeriesBrush", variant);
        }
        if (_cursorLine is not null)
        {
            _cursorLine.Color = ResolveBrushColor("GraphCursorBrush", variant);
        }
    }

    /// <summary>
    /// Replaces the series buffers with the supplied points and applies axis
    /// limits. Returns true if data was applied; false if the series was empty
    /// (in which case the plot is reset).
    /// </summary>
    public bool UpdateSeries(
        IReadOnlyList<BitratePoint> series,
        double? knownDurationSeconds,
        ThemeVariant variant)
    {
        if (series.Count == 0)
        {
            ResetSeries();
            return false;
        }

        EnsureCapacity(series.Count);
        for (var i = 0; i < series.Count; i++)
        {
            _xs[i] = series[i].TimeSeconds;
            _ys[i] = series[i].BitrateMbps;
        }
        _seriesCount = series.Count;

        // Bound the visible range on the existing scatter rather than removing and
        // re-adding it. ScottPlot's render thread doesn't synchronize against
        // plottables-list mutations, so Add/Remove on every refresh races with
        // RegenerateTicks and produces intermittent NREs.
        if (_scatter is not null)
        {
            _scatter.MinRenderIndex = 0;
            _scatter.MaxRenderIndex = _seriesCount - 1;
        }

        _cursorLine ??= _plot.Plot.Add.VerticalLine(0);
        _cursorLine.IsVisible = false;
        _cursorLine.LineWidth = 1;

        ApplySeriesColors(variant);

        ApplyXAxisIfExtentChanged(knownDurationSeconds);
        ApplyYAxisFromData();
        return true;
    }

    public void ResetSeries()
    {
        if (_scatter is not null)
        {
            _plot.Plot.Remove(_scatter);
            _scatter = null;
        }
        if (_cursorLine is not null)
        {
            _plot.Plot.Remove(_cursorLine);
            _cursorLine = null;
        }
        if (_yAxisRule is not null)
        {
            _plot.Plot.Axes.Rules.Remove(_yAxisRule);
            _yAxisRule = null;
        }
        if (_xBoundaryRule is not null)
        {
            _plot.Plot.Axes.Rules.Remove(_xBoundaryRule);
            _xBoundaryRule = null;
        }
        _xs = Array.Empty<double>();
        _ys = Array.Empty<double>();
        _seriesCount = 0;
        _lastAppliedXRight = 0;
        _lastAppliedYTop = 0;
        _yZoomLevel = 1.0;
        _plot.Plot.Axes.AutoScale();
    }

    /// <summary>
    /// Resets the viewport to show the full data range. Prefers the known stream
    /// duration; otherwise falls back to the supplied range or the data extents.
    /// </summary>
    public void ResetViewport(double? knownDurationSeconds, VisibleTimeRange fallback)
    {
        _yZoomLevel = 1.0;

        if (knownDurationSeconds is { } duration && duration > 0)
        {
            _plot.Plot.Axes.SetLimitsX(0, duration);
            UpdateXBoundaryRule(duration);
            _lastAppliedXRight = duration;
        }
        else if (fallback.IsValid)
        {
            var right = fallback.EndSeconds;
            _plot.Plot.Axes.SetLimitsX(Math.Max(0, fallback.StartSeconds), right);
            UpdateXBoundaryRule(right);
            _lastAppliedXRight = right;
        }
        else
        {
            ApplyXAxisFromData(knownDurationSeconds);
        }
        ApplyYAxisFromData();
    }

    public void ShowCursor(double x)
    {
        if (_cursorLine is null) return;
        _cursorLine.X = x;
        _cursorLine.IsVisible = true;
    }

    public void HideCursor()
    {
        if (_cursorLine is null) return;
        _cursorLine.IsVisible = false;
    }

    public double PixelToDataX(Point pixel)
    {
        var p = new Pixel((float)pixel.X, (float)pixel.Y);
        return _plot.Plot.GetCoordinates(p).X;
    }

    public VisibleTimeRange GetVisibleRange()
    {
        var limits = _plot.Plot.Axes.GetLimits();
        return new VisibleTimeRange(limits.Left, limits.Right);
    }

    public void Refresh() => _plot.Refresh();

    private void ApplyXAxisFromData(double? knownDurationSeconds)
    {
        var xRight = ComputeXRight(knownDurationSeconds);
        if (xRight <= 0) return;
        _plot.Plot.Axes.SetLimitsX(0, xRight);
        UpdateXBoundaryRule(xRight);
        _lastAppliedXRight = xRight;
    }

    /// <summary>
    /// Applies the X axis only when the computed extent differs from what we last
    /// applied. Prevents mode/window changes — which re-emit a series with the same
    /// X range — from clobbering the user's current zoom. Progressive loading of a
    /// file with unknown duration still extends X as new data arrives, and an
    /// explicit reset via <see cref="ResetViewport"/> still re-establishes the
    /// full view.
    /// </summary>
    private void ApplyXAxisIfExtentChanged(double? knownDurationSeconds)
    {
        var xRight = ComputeXRight(knownDurationSeconds);
        if (xRight <= 0) return;
        if (Math.Abs(xRight - _lastAppliedXRight) < 1e-9) return;
        _plot.Plot.Axes.SetLimitsX(0, xRight);
        UpdateXBoundaryRule(xRight);
        _lastAppliedXRight = xRight;
    }

    private double ComputeXRight(double? knownDurationSeconds)
    {
        if (knownDurationSeconds is { } duration && duration > 0) return duration;

        if (_seriesCount == 0) return 0;

        double xMax = 0;
        for (var i = 0; i < _seriesCount; i++)
        {
            var v = _xs[i];
            if (!double.IsFinite(v)) continue;
            if (v > xMax) xMax = v;
        }

        if (xMax <= 0) return 0;
        var pad = Math.Max(xMax * 0.01, 0.1);
        return xMax + pad;
    }

    private void UpdateXBoundaryRule(double xRight)
    {
        // Clamp user zoom/pan so the X axis can't extend past [0, xRight].
        // Y bounds here are intentionally wide — the LockedVertical rule enforces Y exactly.
        if (_xBoundaryRule is not null)
        {
            _plot.Plot.Axes.Rules.Remove(_xBoundaryRule);
        }
        var limits = new AxisLimits(0, xRight, double.MinValue / 4, double.MaxValue / 4);
        _xBoundaryRule = new ScottPlot.AxisRules.MaximumBoundary(
            _plot.Plot.Axes.Bottom, _plot.Plot.Axes.Left, limits);
        _plot.Plot.Axes.Rules.Add(_xBoundaryRule);
    }

    private void ApplyYAxisFromData()
    {
        if (_seriesCount == 0) return;

        double yMax = 0;
        for (var i = 0; i < _seriesCount; i++)
        {
            var v = _ys[i];
            if (!double.IsFinite(v)) continue;
            if (v > yMax) yMax = v;
        }

        if (yMax <= 0) yMax = 1;
        var pad = Math.Max(yMax * 0.05, 0.5);
        var yTop = (yMax + pad) * _yZoomLevel;

        if (Math.Abs(yTop - _lastAppliedYTop) < 1e-9) return;

        _plot.Plot.Axes.SetLimitsY(0, yTop);
        if (_yAxisRule is not null)
        {
            _plot.Plot.Axes.Rules.Remove(_yAxisRule);
        }
        _yAxisRule = new ScottPlot.AxisRules.LockedVertical(_plot.Plot.Axes.Left, 0, yTop);
        _plot.Plot.Axes.Rules.Add(_yAxisRule);
        _lastAppliedYTop = yTop;
    }

    public void ApplyYZoom(double zoomLevel)
    {
        _yZoomLevel = zoomLevel;
        _lastAppliedYTop = 0;
        ApplyYAxisFromData();
        Refresh();
    }

    private void EnsureCapacity(int count)
    {
        if (_xs.Length >= count && _scatter is not null) return;

        // Geometric growth so reallocations are O(log n) for the lifetime of a
        // load, not per refresh. The scatter must be replaced when the underlying
        // arrays change, so this is the only place that adds it to the plot.
        var newSize = Math.Max(count, _xs.Length == 0 ? InitialCapacity : _xs.Length * 2);
        _xs = new double[newSize];
        _ys = new double[newSize];

        if (_scatter is not null)
        {
            _plot.Plot.Remove(_scatter);
        }
        _scatter = _plot.Plot.Add.ScatterLine(_xs, _ys);
        _scatter.LineWidth = 1.2f;
        _scatter.MarkerSize = 0;
        _scatter.MaxRenderIndex = -1;
    }

    private static ScottPlot.Color ResolveBrushColor(string key, ThemeVariant variant)
    {
        var app = Application.Current
            ?? throw new InvalidOperationException("Application.Current is null.");
        if (!app.TryGetResource(key, variant, out var value) || value is not Avalonia.Media.ISolidColorBrush brush)
        {
            throw new InvalidOperationException($"Graph theme resource '{key}' is missing or not a SolidColorBrush.");
        }
        var c = brush.Color;
        return new ScottPlot.Color(c.R, c.G, c.B, c.A);
    }

    private static string FormatSecondsAsClock(double seconds)
    {
        if (!double.IsFinite(seconds)) return string.Empty;
        var negative = seconds < 0;
        var ts = TimeSpan.FromSeconds(Math.Abs(seconds));
        var sign = negative ? "-" : string.Empty;
        return ts.TotalHours >= 1
            ? $"{sign}{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{sign}{ts.Minutes:D2}:{ts.Seconds:D2}";
    }
}
