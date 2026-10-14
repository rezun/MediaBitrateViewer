using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using MediaBitrateViewer.App.Views;
using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.Models;
using MediaBitrateViewer.Core.Workflow;
using MediaBitrateViewer.Tests.Fakes;
using MediaBitrateViewer.Tests.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using ScottPlot.Avalonia;
using SkiaSharp;

[assembly: AvaloniaTestApplication(typeof(MediaBitrateViewer.Tests.App.GraphTestApplication))]

namespace MediaBitrateViewer.Tests.App;

public sealed class GraphTestApplication : MediaBitrateViewer.App.App
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<GraphTestApplication>()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    // Test setup supplies only the theme dependency, without starting production
    // update services or loading user preferences from the developer's machine.
    public override void OnFrameworkInitializationCompleted() { }

    internal void SetThemeService(IThemeService service)
    {
        var services = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
        var property = typeof(MediaBitrateViewer.App.App).GetProperty(nameof(Services))
            ?? throw new InvalidOperationException("Missing application services property");
        property.SetValue(this, services);
    }
}

public sealed class BitrateGraphViewTests
{
    [AvaloniaTheory]
    [InlineData(220, 550, 200, 200, 1)]
    [InlineData(550, 220, 280, 120, 1)]
    [InlineData(220, 550, 120, 280, 2)]
    [InlineData(220, 1000, 200, 200, 1)]
    [InlineData(550, -100, 200, 200, 1)]
    public async Task LeftDrag_ZoomsSelectedTimeRange_AndPreservesYZoom(
        double startX, double endX, double startY, double endY, double scale)
    {
        await using var session = await GraphSession.CreateAsync(scale);
        session.Vm.YZoomInCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        session.Render();
        var before = session.Plot.Plot.Axes.GetLimits();
        var beforeSamples = session.Vm.Statistics.Statistics.SampleCount;
        var rect = session.Plot.Plot.RenderManager.LastRender.DataRect;
        var start = new Point(startX, startY);
        var end = new Point(endX, endY);
        var x1 = session.Plot.Plot.Axes.Bottom.GetCoordinate(
            (float)(startX / scale), rect);
        var x2 = session.Plot.Plot.Axes.Bottom.GetCoordinate(
            Math.Clamp((float)(endX / scale), rect.Left, rect.Right), rect);

        session.Window.MouseDown(start, MouseButton.Left);
        session.Window.MouseMove(end);
        Assert.True(session.Plot.Plot.ZoomRectangle.IsVisible);
        Assert.True(session.Plot.Plot.ZoomRectangle.VerticalSpan);
        Assert.Equal(before, session.Plot.Plot.Axes.GetLimits());
        session.Window.MouseUp(end, MouseButton.Left);
        session.Render();

        var after = session.Plot.Plot.Axes.GetLimits();
        Assert.Equal(Math.Min(x1, x2), after.Left, 6);
        Assert.Equal(Math.Max(x1, x2), after.Right, 6);
        Assert.Equal(before.Bottom, after.Bottom);
        Assert.Equal(before.Top, after.Top);
        Assert.False(session.Plot.Plot.ZoomRectangle.IsVisible);
        Assert.Equal(after.Left, session.Vm.VisibleRange.StartSeconds, 6);
        Assert.Equal(after.Right, session.Vm.VisibleRange.EndSeconds, 6);
        Assert.True(session.Vm.Statistics.Statistics.SampleCount < beforeSamples);
        Assert.Equal(session.Vm.VisibleRange, session.Vm.Statistics.Statistics.Range);

        session.Vm.ResetZoomCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        session.Render();
        Assert.Equal(0, session.Plot.Plot.Axes.GetLimits().Left);
        Assert.Equal(session.Vm.KnownDurationSeconds, session.Plot.Plot.Axes.GetLimits().Right);
    }

    [AvaloniaTheory]
    [InlineData(300, 200, 300, 200)]
    [InlineData(300, 200, 302, 200)]
    [InlineData(300, 120, 300, 280)]
    [InlineData(5, 200, 550, 200)]
    public async Task ClickOrInvalidSelection_DoesNotChangeViewport(
        double startX, double startY, double endX, double endY)
    {
        await using var session = await GraphSession.CreateAsync();
        var before = session.Plot.Plot.Axes.GetLimits();
        session.Window.MouseDown(new Point(startX, startY), MouseButton.Left);
        session.Window.MouseMove(new Point(endX, endY));
        session.Window.MouseUp(new Point(endX, endY), MouseButton.Left);
        session.Render();
        Assert.Equal(before, session.Plot.Plot.Axes.GetLimits());
        Assert.False(session.Plot.Plot.ZoomRectangle.IsVisible);
    }

    [AvaloniaFact]
    public async Task Escape_CancelsSelection_AndAllowsAnotherDrag()
    {
        await using var session = await GraphSession.CreateAsync();
        var before = session.Plot.Plot.Axes.GetLimits();
        session.Window.MouseDown(new Point(220, 200), MouseButton.Left);
        session.Window.MouseMove(new Point(550, 200));
        Assert.True(session.Plot.Plot.ZoomRectangle.IsVisible);
        session.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        session.Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        session.Window.MouseUp(new Point(550, 200), MouseButton.Left);
        session.Render();
        Assert.False(session.Plot.Plot.ZoomRectangle.IsVisible);
        Assert.Equal(before, session.Plot.Plot.Axes.GetLimits());

        // Start elsewhere so this is a new drag, rather than a double-click reset.
        session.Window.MouseDown(new Point(300, 200), MouseButton.Left);
        session.Window.MouseMove(new Point(600, 200));
        session.Window.MouseUp(new Point(600, 200), MouseButton.Left);
        session.Render();
        Assert.True(session.Plot.Plot.Axes.GetLimits().HorizontalSpan < before.HorizontalSpan);
    }

    [AvaloniaFact]
    public async Task ResetDuringSelection_ClearsOverlay_AndIgnoresRelease()
    {
        await using var session = await GraphSession.CreateAsync();
        var before = session.Plot.Plot.Axes.GetLimits();
        session.Window.MouseDown(new Point(220, 200), MouseButton.Left);
        session.Window.MouseMove(new Point(550, 200));
        Assert.True(session.Plot.Plot.ZoomRectangle.IsVisible);
        session.Vm.ResetZoomCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        session.Window.MouseUp(new Point(550, 200), MouseButton.Left);
        session.Render();
        Assert.False(session.Plot.Plot.ZoomRectangle.IsVisible);
        Assert.Equal(before, session.Plot.Plot.Axes.GetLimits());
    }

    [AvaloniaFact]
    public async Task MiddleDrag_PansZoomedRangeHorizontally()
    {
        await using var session = await GraphSession.CreateAsync();
        session.Window.MouseDown(new Point(220, 200), MouseButton.Left);
        session.Window.MouseMove(new Point(550, 200));
        session.Window.MouseUp(new Point(550, 200), MouseButton.Left);
        session.Render();
        var before = session.Plot.Plot.Axes.GetLimits();

        session.Window.MouseDown(new Point(400, 200), MouseButton.Middle);
        session.Window.MouseMove(new Point(450, 240));
        session.Window.MouseUp(new Point(450, 240), MouseButton.Middle);
        session.Render();
        var after = session.Plot.Plot.Axes.GetLimits();
        Assert.True(after.Left < before.Left);
        Assert.Equal(before.HorizontalSpan, after.HorizontalSpan, 6);
        Assert.Equal(before.Bottom, after.Bottom);
        Assert.Equal(before.Top, after.Top);
        Assert.Equal(after.Left, session.Vm.VisibleRange.StartSeconds, 6);
    }

    [AvaloniaFact]
    public async Task ClosingWindow_UnsubscribesGraphFromGlobalThemeService()
    {
        var harness = new MainWindowViewModelHarness();
        var app = Assert.IsType<GraphTestApplication>(Application.Current);
        app.SetThemeService(harness.Theme);
        await using var vm = harness.Build();
        var graph = new BitrateGraphView { DataContext = vm };
        var window = new Window { Content = graph, Width = 800, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, harness.Theme.SubscriberCount);

        // Reattachment must restore exactly one subscription.
        window.Content = null;
        Assert.Equal(0, harness.Theme.SubscriberCount);
        window.Content = graph;
        Assert.Equal(1, harness.Theme.SubscriberCount);
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, harness.Theme.SubscriberCount);
    }

    [AvaloniaFact]
    public async Task ScrollZoom_WithStationaryPointer_UpdatesRangeAndStatistics()
    {
        var harness = new MainWindowViewModelHarness();
        var app = Assert.IsType<GraphTestApplication>(Application.Current);
        app.SetThemeService(harness.Theme);
        var fingerprint = await harness.Fingerprint.ComputeAsync("/tmp/sample.mp4", CancellationToken.None);
        harness.Cache.SeedCompletedFrameAnalysis(fingerprint, 0,
            Enumerable.Range(0, 60).Select(i => new FrameRecord
            {
                TimestampSeconds = i, DurationSeconds = 1, PacketSizeBytes = (i + 1) * 1000
            }).ToArray());
        await using var vm = harness.Build();
        await vm.InitializeAsync(CancellationToken.None);
        await vm.LoadFileAsync("/tmp/sample.mp4");
        await MainWindowViewModelHarness.WaitForStatusAsync(vm, WorkflowStatus.Ready);

        var graph = new BitrateGraphView { DataContext = vm };
        var window = new Window { Content = graph, Width = 800, Height = 400 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var plot = graph.FindControl<AvaPlot>("Plot") ?? throw new InvalidOperationException("Missing plot");
            window.UpdateLayout();
            using var surface = SKSurface.Create(new SKImageInfo(800, 400))
                ?? throw new InvalidOperationException("Could not create test render surface");
            // Hit testing uses the control's multiplot layout. Rendering Plot alone
            // would leave that layout empty and wheel input would be ignored.
            plot.Multiplot.Render(surface.Canvas, new ScottPlot.PixelRect(800, 400));
            window.MouseMove(new Point(400, 200));
            Dispatcher.UIThread.RunJobs();
            var before = vm.VisibleRange;
            var beforeSamples = vm.Statistics.Statistics.SampleCount;

            window.MouseWheel(new Point(400, 200), new Vector(0, 3));
            plot.Multiplot.Render(surface.Canvas, new ScottPlot.PixelRect(800, 400));
            Dispatcher.UIThread.RunJobs();

            var limits = plot.Plot.Axes.GetLimits();
            Assert.Equal(limits.Left, vm.VisibleRange.StartSeconds, 6);
            Assert.Equal(limits.Right, vm.VisibleRange.EndSeconds, 6);
            Assert.True(vm.VisibleRange.EndSeconds - vm.VisibleRange.StartSeconds < before.EndSeconds - before.StartSeconds,
                $"Before={before}; after={vm.VisibleRange}; plot bounds={plot.Bounds}");
            Assert.True(vm.Statistics.Statistics.SampleCount < beforeSamples);
            Assert.Equal(vm.VisibleRange, vm.Statistics.Statistics.Range);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed class GraphSession : IAsyncDisposable
    {
        public required MediaBitrateViewer.Core.ViewModels.MainWindowViewModel Vm { get; init; }
        public required Window Window { get; init; }
        public required AvaPlot Plot { get; init; }
        public required SKSurface Surface { get; init; }

        public static async Task<GraphSession> CreateAsync(double scale = 1)
        {
            var harness = new MainWindowViewModelHarness();
            Assert.IsType<GraphTestApplication>(Application.Current).SetThemeService(harness.Theme);
            var fingerprint = await harness.Fingerprint.ComputeAsync("/tmp/sample.mp4", CancellationToken.None);
            harness.Cache.SeedCompletedFrameAnalysis(fingerprint, 0,
                Enumerable.Range(0, 60).Select(i => new FrameRecord
                {
                    TimestampSeconds = i, DurationSeconds = 1, PacketSizeBytes = (i + 1) * 1000
                }).ToArray());
            var vm = harness.Build();
            await vm.InitializeAsync(CancellationToken.None);
            await vm.LoadFileAsync("/tmp/sample.mp4");
            await MainWindowViewModelHarness.WaitForStatusAsync(vm, WorkflowStatus.Ready);
            var graph = new BitrateGraphView { DataContext = vm };
            var window = new Window { Content = graph, Width = 800, Height = 400 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var plot = graph.FindControl<AvaPlot>("Plot") ?? throw new InvalidOperationException("Missing plot");
            plot.Plot.ScaleFactor = scale;
            var session = new GraphSession
            {
                Vm = vm, Window = window, Plot = plot,
                Surface = SKSurface.Create(new SKImageInfo(800, 400))
                    ?? throw new InvalidOperationException("Could not create test render surface")
            };
            session.Render();
            return session;
        }

        public void Render()
        {
            Plot.Multiplot.Render(Surface.Canvas, new ScottPlot.PixelRect((float)Plot.Bounds.Width, (float)Plot.Bounds.Height));
            Dispatcher.UIThread.RunJobs();
        }

        public async ValueTask DisposeAsync()
        {
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Surface.Dispose();
            await Vm.DisposeAsync();
        }
    }
}
