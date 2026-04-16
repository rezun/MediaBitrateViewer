using System.ComponentModel;
using MediaBitrateViewer.Core.Analysis;
using MediaBitrateViewer.Core.ViewModels;
using MediaBitrateViewer.Core.Workflow;
using MediaBitrateViewer.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaBitrateViewer.Tests.ViewModels;

/// <summary>
/// Test-only builder that centralizes MainWindowViewModel construction so each test
/// sets only the fakes it cares about instead of threading fourteen constructor args.
/// </summary>
internal sealed class MainWindowViewModelHarness
{
    public FakeAppUpdateService Updates { get; } = new();
    public FakeAppRuntimeInfo Runtime { get; } = new();
    public FakeFfprobeLocator FfprobeLocator { get; set; } = new(true);
    public FakeFingerprintService Fingerprint { get; } = new();
    public FakeProbeService Probe { get; } = new();
    public FakeFrameAnalysisService Frames { get; } = new();
    public FakeCache Cache { get; } = new();
    public FakePrefsStore Prefs { get; } = new();
    public FakeThemeService Theme { get; } = new();
    public FakeFilePicker Picker { get; } = new();
    public FakeWindowCoordinator Coord { get; } = new();
    public FakeRecentFilesService Recent { get; } = new();
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    public MainWindowViewModel Build()
    {
        var pipeline = new AnalysisPipelineService(
            Fingerprint, Probe, Frames, Cache,
            TimeProvider,
            NullLogger<AnalysisPipelineService>.Instance);

        return new MainWindowViewModel(
            Updates,
            Runtime,
            FfprobeLocator, pipeline,
            Prefs, Theme, Picker, Coord, Recent,
            TimeProvider,
            NullLogger<MainWindowViewModel>.Instance,
            new CursorReadoutViewModel(),
            new StatisticsPanelViewModel(),
            new StreamMetadataViewModel(),
            new LoadingPanelViewModel());
    }

    public static async Task WaitForStatusAsync(MainWindowViewModel vm, WorkflowStatus expected, TimeSpan? timeout = null)
    {
        if (vm.Status == expected) return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.Status) && vm.Status == expected)
                tcs.TrySetResult();
        };
        vm.PropertyChanged += handler;
        try
        {
            if (vm.Status == expected) return;
            await tcs.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5));
        }
        finally
        {
            vm.PropertyChanged -= handler;
        }
    }
}
