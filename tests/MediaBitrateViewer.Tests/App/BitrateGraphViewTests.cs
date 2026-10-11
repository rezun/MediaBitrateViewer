using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
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
}
