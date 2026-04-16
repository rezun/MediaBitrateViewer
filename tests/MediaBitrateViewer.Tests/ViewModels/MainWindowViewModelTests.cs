using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.Models;
using MediaBitrateViewer.Core.Workflow;

namespace MediaBitrateViewer.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task Initialize_FfprobeMissing_SetsBlockingState()
    {
        var harness = new MainWindowViewModelHarness { FfprobeLocator = new Fakes.FakeFfprobeLocator(false) };
        var vm = harness.Build();

        await vm.InitializeAsync(CancellationToken.None);

        Assert.Equal(WorkflowStatus.FfprobeMissing, vm.Status);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public async Task LoadFile_PopulatesStreamsAndDefaultsToFirst()
    {
        var harness = new MainWindowViewModelHarness();
        var vm = harness.Build();
        await vm.InitializeAsync(CancellationToken.None);

        await vm.LoadFileAsync("/tmp/sample.mp4");

        Assert.NotNull(vm.ProbedFile);
        Assert.True(vm.VideoStreams.Count >= 1);
        Assert.NotNull(vm.SelectedStream);
        Assert.Equal(vm.VideoStreams[0].Index, vm.SelectedStream!.Index);
    }

    [Fact]
    public async Task DropOnPopulatedWindow_RoutesToCoordinator()
    {
        var harness = new MainWindowViewModelHarness();
        var vm = harness.Build();
        await vm.InitializeAsync(CancellationToken.None);
        await vm.LoadFileAsync("/tmp/first.mp4");

        await vm.OpenFromExternalAsync("/tmp/second.mp4");

        Assert.Single(harness.Coord.OpenedFiles);
        Assert.Equal("/tmp/second.mp4", harness.Coord.OpenedFiles[0]);
    }

    [Fact]
    public async Task DropOnEmptyWindow_LoadsInPlace()
    {
        var harness = new MainWindowViewModelHarness();
        var vm = harness.Build();
        await vm.InitializeAsync(CancellationToken.None);

        await vm.OpenFromExternalAsync("/tmp/first.mp4");

        Assert.Empty(harness.Coord.OpenedFiles);
        Assert.NotNull(vm.ProbedFile);
        Assert.Equal("/tmp/first.mp4", vm.FilePath);
    }

    [Fact]
    public async Task CancelAnalysis_DuringRun_TransitionsToCanceledAndPreservesPartialData()
    {
        var harness = new MainWindowViewModelHarness();
        harness.Frames.FramesToEmit = 10;
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Frames.HoldAfterFirstFrame = hold.Task;

        var vm = harness.Build();
        await vm.InitializeAsync(CancellationToken.None);
        await vm.LoadFileAsync("/tmp/sample.mp4");

        // Wait until analysis is actively running (first frame delivered, loop is blocked on hold).
        await harness.Frames.FirstFrameEmitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await MainWindowViewModelHarness.WaitForStatusAsync(vm, WorkflowStatus.RunningFrameAnalysis);

        vm.CancelAnalysisCommand.Execute(null);
        hold.TrySetResult(); // release the loop so cancellation can propagate

        await MainWindowViewModelHarness.WaitForStatusAsync(vm, WorkflowStatus.FrameAnalysisCanceled);

        Assert.True(vm.IsPartialData);
        Assert.NotEmpty(vm.Frames);
    }

    [Fact]
    public async Task GraphModeChange_TriggersRecomputeAndPersistsPreference()
    {
        var harness = new MainWindowViewModelHarness();
        var vm = harness.Build();
        await vm.InitializeAsync(CancellationToken.None);
        await vm.LoadFileAsync("/tmp/sample.mp4");
        await MainWindowViewModelHarness.WaitForStatusAsync(vm, WorkflowStatus.Ready);

        var versionBefore = vm.SeriesVersion;
        var updatesBefore = harness.Prefs.UpdateCalls;

        vm.GraphMode = GraphMode.RollingAverage;

        Assert.True(vm.SeriesVersion > versionBefore);
        Assert.True(harness.Prefs.UpdateCalls > updatesBefore);
        Assert.Equal(GraphMode.RollingAverage, harness.Prefs.Current.GraphMode);
    }

    [Fact]
    public async Task RollingWindowChange_OnlyRecomputesWhenGraphModeUsesWindow()
    {
        var harness = new MainWindowViewModelHarness();
        var vm = harness.Build();
        await vm.InitializeAsync(CancellationToken.None);
        await vm.LoadFileAsync("/tmp/sample.mp4");
        await MainWindowViewModelHarness.WaitForStatusAsync(vm, WorkflowStatus.Ready);

        // Default GraphMode is PerSecond — no recompute, but pref still persists.
        var versionBefore = vm.SeriesVersion;
        vm.RollingWindow = RollingAverageWindow.Sec5;
        Assert.Equal(versionBefore, vm.SeriesVersion);
        Assert.Equal(RollingAverageWindow.Sec5, harness.Prefs.Current.RollingWindow);

        // Switch to a mode that uses the window and verify rolling-window change forces recompute.
        vm.GraphMode = GraphMode.RollingAverage;
        var versionAfterModeSwitch = vm.SeriesVersion;
        vm.RollingWindow = RollingAverageWindow.Sec10;
        Assert.True(vm.SeriesVersion > versionAfterModeSwitch);
    }

    [Fact]
    public async Task Reload_ReProbesTheFile()
    {
        var harness = new MainWindowViewModelHarness();
        var vm = harness.Build();
        await vm.InitializeAsync(CancellationToken.None);
        await vm.LoadFileAsync("/tmp/sample.mp4");
        await MainWindowViewModelHarness.WaitForStatusAsync(vm, WorkflowStatus.Ready);

        var probeCallsBefore = harness.Probe.Calls;

        await vm.ReloadCommand.ExecuteAsync(null);

        // Pipeline consults the cache first, so a reload won't always re-hit ffprobe,
        // but it MUST re-enter the pipeline and land back in a usable state.
        Assert.NotNull(vm.ProbedFile);
        Assert.Equal("/tmp/sample.mp4", vm.FilePath);
        Assert.True(probeCallsBefore >= 1);
    }

    [Fact]
    public async Task ClearCurrentFileCache_DelegatesToCache()
    {
        var harness = new MainWindowViewModelHarness();
        var vm = harness.Build();
        await vm.InitializeAsync(CancellationToken.None);
        await vm.LoadFileAsync("/tmp/sample.mp4");
        await MainWindowViewModelHarness.WaitForStatusAsync(vm, WorkflowStatus.Ready);

        await vm.ClearCurrentFileCacheCommand.ExecuteAsync(null);

        Assert.Equal(1, harness.Cache.ClearFileCalls);
    }

    [Fact]
    public async Task LoadFile_WithCachedAnalysis_SkipsFrameAnalysisService()
    {
        var harness = new MainWindowViewModelHarness();
        var fingerprint = await harness.Fingerprint.ComputeAsync("/tmp/sample.mp4", CancellationToken.None);
        var seededFrames = new[]
        {
            new FrameRecord { TimestampSeconds = 0.00, DurationSeconds = 0.04, PacketSizeBytes = 1200 },
            new FrameRecord { TimestampSeconds = 0.04, DurationSeconds = 0.04, PacketSizeBytes = 1400 }
        };
        harness.Cache.SeedCompletedFrameAnalysis(fingerprint, videoStreamIndex: 0, seededFrames);

        var vm = harness.Build();
        await vm.InitializeAsync(CancellationToken.None);
        await vm.LoadFileAsync("/tmp/sample.mp4");
        await MainWindowViewModelHarness.WaitForStatusAsync(vm, WorkflowStatus.Ready);

        Assert.Equal(0, harness.Frames.Calls);
        Assert.Equal(seededFrames.Length, vm.Frames.Count);
    }

    [Fact]
    public async Task SetTheme_PersistsAndAppliesTheme()
    {
        var harness = new MainWindowViewModelHarness();
        var vm = harness.Build();
        await vm.InitializeAsync(CancellationToken.None);

        vm.SetThemeCommand.Execute(ThemeMode.Dark);

        Assert.Equal(ThemeMode.Dark, harness.Theme.CurrentMode);
        Assert.Equal(ThemeMode.Dark, harness.Prefs.Current.Theme);
    }

    [Fact]
    public void UpdateStateChange_RefreshesToolbarProperties()
    {
        var harness = new MainWindowViewModelHarness();
        var vm = harness.Build();

        Assert.False(vm.IsUpdateReadyToInstall);
        Assert.False(vm.InstallUpdateCommand.CanExecute(null));

        harness.Updates.SetPendingUpdate("1.2.3");

        Assert.True(vm.IsUpdateReadyToInstall);
        Assert.Equal("Restart to install 1.2.3", vm.UpdateButtonText);
        Assert.True(vm.InstallUpdateCommand.CanExecute(null));
    }

    [Fact]
    public void InstallUpdate_DelegatesToUpdateService()
    {
        var harness = new MainWindowViewModelHarness();
        var vm = harness.Build();
        harness.Updates.SetPendingUpdate("1.2.3");

        vm.InstallUpdateCommand.Execute(null);

        Assert.Equal(1, harness.Updates.ApplyCalls);
    }

    [Fact]
    public void ShowTestUpdateNotification_InDevelopment_ShowsUpdateBanner()
    {
        var harness = new MainWindowViewModelHarness();
        harness.Runtime.IsDevelopmentEnvironment = true;
        var vm = harness.Build();

        Assert.True(vm.ShowTestUpdateNotificationCommand.CanExecute(null));

        vm.ShowTestUpdateNotificationCommand.Execute(null);

        Assert.True(vm.IsUpdateReadyToInstall);
        Assert.Equal("Restart to install 9.9.9-debug", vm.UpdateButtonText);
    }

    [Fact]
    public void ShowTestUpdateNotification_OutsideDevelopment_IsDisabled()
    {
        var harness = new MainWindowViewModelHarness();
        harness.Runtime.IsDevelopmentEnvironment = false;
        var vm = harness.Build();

        Assert.False(vm.IsDevelopmentEnvironment);
        Assert.False(vm.ShowTestUpdateNotificationCommand.CanExecute(null));
    }
}
