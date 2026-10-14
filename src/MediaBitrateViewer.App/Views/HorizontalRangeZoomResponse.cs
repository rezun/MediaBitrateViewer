using ScottPlot;
using ScottPlot.Interactivity;
using ScottPlot.Interactivity.UserActions;

namespace MediaBitrateViewer.App.Views;

/// <summary>Left-drag selects a time range without changing the vertical axes.</summary>
internal sealed class HorizontalRangeZoomResponse(Func<bool> hasSeries) : IUserActionResponse
{
    private const float MinimumDragWidth = 5;
    private static readonly Key EscapeKey = new(nameof(Avalonia.Input.Key.Escape));
    private Plot? _plot;
    private Pixel _start;

    public void ResetState(IPlotControl plotControl)
    {
        if (_plot is not null)
            _plot.ZoomRectangle.IsVisible = false;
        _plot = null;
    }

    public ResponseInfo Execute(IPlotControl plotControl, IUserAction userAction, KeyboardState keys)
    {
        if (userAction is KeyDown keyDown && keyDown.Key == EscapeKey)
        {
            var wasSelecting = _plot is not null;
            ResetState(plotControl);
            return new ResponseInfo { RefreshNeeded = wasSelecting };
        }

        if (userAction is IMouseButtonAction { IsPressed: true } down &&
            down.Button == StandardMouseButtons.Left && hasSeries())
        {
            var plot = plotControl.GetPlotAtPixel(down.Pixel);
            if (plot is null) return ResponseInfo.NoActionRequired;
            var pixel = down.Pixel.Divide((float)plot.ScaleFactor);
            var rect = plot.RenderManager.LastRender.DataRect;
            if (!rect.HasArea || !rect.Contains(pixel)) return ResponseInfo.NoActionRequired;

            _plot = plot;
            _start = pixel;
            return new ResponseInfo { IsPrimary = true };
        }

        if (_plot is null) return ResponseInfo.NoActionRequired;

        if (userAction is IMouseAction mouse)
        {
            if (userAction is IMouseButtonAction button &&
                (button.IsPressed || button.Button != StandardMouseButtons.Left))
                return new ResponseInfo { IsPrimary = true };

            var rect = _plot.RenderManager.LastRender.DataRect;
            var pixel = mouse.Pixel.Divide((float)_plot.ScaleFactor);
            var x = Math.Clamp(pixel.X, rect.Left, rect.Right);
            var wideEnough = Math.Abs(x - _start.X) >= MinimumDragWidth;
            var selection = _plot.ZoomRectangle;
            selection.MouseDown = new Pixel(_start.X, rect.Top);
            selection.MouseUp = new Pixel(x, rect.Bottom);
            selection.HorizontalSpan = false;
            selection.VerticalSpan = true;
            selection.IsVisible = wideEnough;

            if (userAction is IMouseButtonAction { IsPressed: false })
            {
                if (wideEnough)
                    selection.Apply(_plot.Axes.Bottom);
                ResetState(plotControl);
                return ResponseInfo.Refresh;
            }

            return new ResponseInfo { RefreshNeeded = true, IsPrimary = true };
        }

        // Keep wheel/key navigation from changing the viewport during selection.
        return new ResponseInfo { IsPrimary = true };
    }
}
