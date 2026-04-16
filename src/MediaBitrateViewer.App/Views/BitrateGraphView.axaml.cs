using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.Models;
using MediaBitrateViewer.Core.ViewModels;

namespace MediaBitrateViewer.App.Views;

public partial class BitrateGraphView : UserControl
{
    private readonly BitratePlotController _controller;
    private MainWindowViewModel? _vm;
    private bool _suppressRangeEvent;
    private long _lastSeriesVersion = -1;

    public BitrateGraphView()
    {
        InitializeComponent();
        _controller = new BitratePlotController(Plot);

        DataContextChanged += OnDataContextChanged;
        Plot.PointerMoved += OnPlotPointerMoved;
        Plot.PointerExited += OnPlotPointerExited;
        Plot.DoubleTapped += OnPlotDoubleTapped;

        _controller.ApplyChrome(CurrentVariant());
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.GraphSeriesUpdated -= OnSeriesUpdated;
            _vm.RequestResetZoom -= OnRequestResetZoom;
            _vm.YZoomChanged -= OnYZoomChanged;
        }

        _vm = DataContext as MainWindowViewModel;

        if (_vm is not null)
        {
            _vm.GraphSeriesUpdated += OnSeriesUpdated;
            _vm.RequestResetZoom += OnRequestResetZoom;
            _vm.YZoomChanged += OnYZoomChanged;
            _controller.ResetSeries();
            _lastSeriesVersion = -1;
        }

        if (Application.Current is App app)
        {
            if (app.Services?.GetService(typeof(IThemeService)) is IThemeService theme)
            {
                theme.AppliedThemeChanged -= OnThemeChanged;
                theme.AppliedThemeChanged += OnThemeChanged;
            }
        }
    }

    private void OnThemeChanged(object? sender, AppliedTheme e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _controller.ApplyChrome(e == AppliedTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light);
            _controller.Refresh();
        });
    }

    private void OnSeriesUpdated(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RebuildAndRefresh, DispatcherPriority.Background);
    }

    private void OnRequestResetZoom(object? sender, VisibleTimeRange e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _suppressRangeEvent = true;
            _controller.ResetViewport(_vm?.KnownDurationSeconds, e);
            _controller.Refresh();
            _suppressRangeEvent = false;
        });
    }

    private void OnYZoomChanged(object? sender, double zoomLevel)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _controller.ApplyYZoom(zoomLevel);
        });
    }

    private void RebuildAndRefresh()
    {
        if (_vm is null) return;

        if (_vm.SeriesVersion == _lastSeriesVersion) return;
        _lastSeriesVersion = _vm.SeriesVersion;

        _suppressRangeEvent = true;
        var applied = _controller.UpdateSeries(_vm.Series, _vm.KnownDurationSeconds, CurrentVariant());
        _controller.Refresh();
        _suppressRangeEvent = false;

        if (applied)
        {
            PublishVisibleRange();
        }
    }

    private void OnPlotPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_vm is null || _controller.SeriesCount == 0) return;
        var t = _controller.PixelToDataX(e.GetPosition(Plot));

        _vm.OnCursorMoved(t);
        _controller.ShowCursor(t);

        PublishVisibleRange();
        _controller.Refresh();
    }

    private void OnPlotPointerExited(object? sender, PointerEventArgs e)
    {
        _controller.HideCursor();
        _controller.Refresh();
    }

    private void OnPlotDoubleTapped(object? sender, TappedEventArgs e)
    {
        _vm?.ResetZoomCommand.Execute(null);
    }

    private void PublishVisibleRange()
    {
        if (_vm is null || _suppressRangeEvent) return;
        _vm.UpdateVisibleRange(_controller.GetVisibleRange());
    }

    private static ThemeVariant CurrentVariant() =>
        Application.Current?.ActualThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
}
