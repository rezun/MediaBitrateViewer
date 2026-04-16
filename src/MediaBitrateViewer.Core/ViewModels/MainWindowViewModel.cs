using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.Analysis;
using MediaBitrateViewer.Core.Models;
using MediaBitrateViewer.Core.Workflow;
using Microsoft.Extensions.Logging;

namespace MediaBitrateViewer.Core.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IAsyncInitializable, IAsyncDisposable
{
    private readonly IAppUpdateService _appUpdateService;
    private readonly IAppRuntimeInfo _appRuntimeInfo;
    private readonly IFfprobeLocator _ffprobeLocator;
    private readonly IAnalysisPipelineService _pipeline;
    private readonly IUserPreferencesStore _preferencesStore;
    private readonly IThemeService _themeService;
    private readonly IFilePickerService _filePickerService;
    private readonly IWindowCoordinator _windowCoordinator;
    private readonly IRecentFilesService _recentFilesService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MainWindowViewModel> _logger;

    private const double MinYZoom = 1.0 / 64.0;

    private CancellationTokenSource? _analysisCts;
    private readonly List<FrameRecord> _frames = new();
    private IReadOnlyList<FrameRecord>? _sortedFrames;
    private IReadOnlyList<BitratePoint> _series = Array.Empty<BitratePoint>();
    private FfprobeLocation? _ffprobeLocation;
    private bool _disposed;
    private double _yZoomLevel = 1.0;

    [ObservableProperty] private string _windowTitle = "Media Bitrate Viewer";
    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private string? _fileDisplayName;
    [ObservableProperty] private WorkflowStatus _status = WorkflowStatus.Idle;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isPartialData;
    [ObservableProperty] private ProbedMediaFile? _probedFile;
    [ObservableProperty] private VideoStreamInfo? _selectedStream;
    [ObservableProperty] private GraphMode _graphMode = GraphMode.PerSecond;
    [ObservableProperty] private RollingAverageWindow _rollingWindow = RollingAverageWindow.Sec1;
    [ObservableProperty] private ThemeMode _themeMode = ThemeMode.System;
    [ObservableProperty] private VisibleTimeRange _visibleRange;
    [ObservableProperty] private long _seriesVersion;
    [ObservableProperty] private double? _knownDurationSeconds;

    public ObservableCollection<VideoStreamInfo> VideoStreams { get; } = new();

    public CursorReadoutViewModel CursorReadout { get; }
    public StatisticsPanelViewModel Statistics { get; }
    public StreamMetadataViewModel StreamMetadata { get; }
    public LoadingPanelViewModel LoadingPanel { get; }

    // Enum.GetValues sorts by underlying numeric value, which matches the desired display order.
    public IReadOnlyList<RollingAverageWindow> AllRollingWindows { get; } = Enum.GetValues<RollingAverageWindow>();
    public IReadOnlyList<GraphMode> AllGraphModes { get; } = Enum.GetValues<GraphMode>();
    public IReadOnlyList<ThemeMode> AllThemeModes { get; } = Enum.GetValues<ThemeMode>();

    public System.Collections.ObjectModel.ReadOnlyObservableCollection<string> RecentFiles => _recentFilesService.Files;

    public bool IsThemeSystem => ThemeMode == ThemeMode.System;
    public bool IsThemeLight => ThemeMode == ThemeMode.Light;
    public bool IsThemeDark => ThemeMode == ThemeMode.Dark;

    public IReadOnlyList<BitratePoint> Series => _series;
    public IReadOnlyList<FrameRecord> Frames => _frames;

    public bool HasFile => ProbedFile is not null;
    public bool HasMultipleStreams => VideoStreams.Count > 1;
    public bool IsDevelopmentEnvironment => _appRuntimeInfo.IsDevelopmentEnvironment;
    public bool ShowFfprobeMissingError => Status == WorkflowStatus.FfprobeMissing;
    public bool ShowGenericError =>
        Status is WorkflowStatus.ProbeFailed or WorkflowStatus.NoVideoStreams or WorkflowStatus.FrameAnalysisFailed;
    public bool ShowIdleHint => Status == WorkflowStatus.Idle;
    public bool HasWindowSetting => GraphMode is GraphMode.RollingAverage or GraphMode.PeakEnvelope;
    public bool IsUpdateReadyToInstall => _appUpdateService.IsUpdateReadyToInstall;
    public string UpdateButtonText => _appUpdateService.UpdateButtonText;
    public string FfprobeVersionDisplay =>
        _ffprobeLocation?.VersionString ?? string.Empty;

    public event EventHandler? GraphSeriesUpdated;
    public event EventHandler<VisibleTimeRange>? RequestResetZoom;
    public event EventHandler<double>? YZoomChanged;

    public MainWindowViewModel(
        IAppUpdateService appUpdateService,
        IAppRuntimeInfo appRuntimeInfo,
        IFfprobeLocator ffprobeLocator,
        IAnalysisPipelineService pipeline,
        IUserPreferencesStore preferencesStore,
        IThemeService themeService,
        IFilePickerService filePickerService,
        IWindowCoordinator windowCoordinator,
        IRecentFilesService recentFilesService,
        TimeProvider timeProvider,
        ILogger<MainWindowViewModel> logger,
        CursorReadoutViewModel cursorReadout,
        StatisticsPanelViewModel statistics,
        StreamMetadataViewModel streamMetadata,
        LoadingPanelViewModel loadingPanel)
    {
        ArgumentNullException.ThrowIfNull(appUpdateService);
        ArgumentNullException.ThrowIfNull(appRuntimeInfo);
        ArgumentNullException.ThrowIfNull(ffprobeLocator);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(preferencesStore);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(filePickerService);
        ArgumentNullException.ThrowIfNull(windowCoordinator);
        ArgumentNullException.ThrowIfNull(recentFilesService);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _appUpdateService = appUpdateService;
        _appRuntimeInfo = appRuntimeInfo;
        _ffprobeLocator = ffprobeLocator;
        _pipeline = pipeline;
        _preferencesStore = preferencesStore;
        _themeService = themeService;
        _filePickerService = filePickerService;
        _windowCoordinator = windowCoordinator;
        _recentFilesService = recentFilesService;
        _timeProvider = timeProvider;
        _logger = logger;
        CursorReadout = cursorReadout;
        Statistics = statistics;
        StreamMetadata = streamMetadata;
        LoadingPanel = loadingPanel;

        _appUpdateService.StateChanged += OnAppUpdateStateChanged;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var prefs = await _preferencesStore.LoadAsync(cancellationToken);
        ThemeMode = prefs.Theme;
        GraphMode = prefs.GraphMode;
        // Guard against stale persisted windows that were removed (e.g. the old 250 ms option).
        RollingWindow = Enum.IsDefined(prefs.RollingWindow) ? prefs.RollingWindow : UserPreferences.Default.RollingWindow;
        _themeService.Apply(prefs.Theme);

        _ffprobeLocation = await _ffprobeLocator.LocateAsync(cancellationToken);
        OnPropertyChanged(nameof(FfprobeVersionDisplay));
        if (!_ffprobeLocation.IsAvailable)
        {
            Status = WorkflowStatus.FfprobeMissing;
            ErrorMessage = "ffprobe was not found on the system PATH. Install FFmpeg and ensure ffprobe is reachable, then restart the app.";
        }
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var path = await _filePickerService.PickVideoFileAsync(CancellationToken.None);
        if (string.IsNullOrEmpty(path)) return;
        await OpenFromExternalAsync(path);
    }

    [RelayCommand]
    private async Task OpenRecentAsync(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        await OpenFromExternalAsync(path);
    }

    [RelayCommand]
    private async Task ClearRecentAsync()
    {
        await _recentFilesService.ClearAsync();
    }

    [RelayCommand]
    private void SetTheme(ThemeMode mode)
    {
        ThemeMode = mode;
    }

    /// <summary>
    /// Public entry from window coordinator when a file is dropped or picked into this window.
    /// If this window already has a file loaded, route to a new window instead.
    /// </summary>
    public async Task OpenFromExternalAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (HasFile)
        {
            _windowCoordinator.OpenWindowFor(path);
            return;
        }
        await LoadFileAsync(path);
    }

    public async Task LoadFileAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (Status == WorkflowStatus.FfprobeMissing) return;

        CancelAnalysisInternal();

        FilePath = path;
        FileDisplayName = Path.GetFileName(path);
        WindowTitle = $"{FileDisplayName} — Media Bitrate Viewer";
        ErrorMessage = null;
        IsPartialData = false;
        ResetSeries();

        try
        {
            Status = WorkflowStatus.ProbingFile;
            var probe = await _pipeline.ProbeAsync(path, CancellationToken.None);

            ProbedFile = probe;
            StreamMetadata.File = probe;

            await _recentFilesService.AddAsync(path);

            if (probe.VideoStreams.Count == 0)
            {
                Status = WorkflowStatus.NoVideoStreams;
                ErrorMessage = "This file does not contain any video streams.";
                return;
            }

            VideoStreams.Clear();
            foreach (var s in probe.VideoStreams) VideoStreams.Add(s);
            OnPropertyChanged(nameof(HasMultipleStreams));

            Status = WorkflowStatus.SelectingStream;
            SelectedStream = VideoStreams[0];
        }
        catch (FileProbeException ex)
        {
            _logger.LogError(ex, "Probe failed for {Path}", path);
            Status = WorkflowStatus.ProbeFailed;
            ErrorMessage = $"Could not probe the file: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error probing {Path}", path);
            Status = WorkflowStatus.ProbeFailed;
            ErrorMessage = $"Unexpected error: {ex.Message}";
        }
    }

    partial void OnSelectedStreamChanged(VideoStreamInfo? value)
    {
        StreamMetadata.Stream = value;
        if (value is null || ProbedFile is null) return;

        // Fire and forget by design: selection change triggers async analysis.
        // We wrap in try/catch to log any uncaught exception.
        _ = StartAnalysisAsync(ProbedFile, value);
    }

    private async Task StartAnalysisAsync(ProbedMediaFile probe, VideoStreamInfo stream)
    {
        try
        {
            CancelAnalysisInternal();
            ResetSeries();

            KnownDurationSeconds = stream.Duration?.TotalSeconds ?? probe.Duration?.TotalSeconds;

            var cached = await _pipeline.TryGetCachedAnalysisAsync(probe.Fingerprint, stream.Index, CancellationToken.None);
            if (cached is not null)
            {
                Status = WorkflowStatus.LoadingCachedAnalysis;
                _frames.AddRange(cached.Frames);
                _sortedFrames = null;
                RecomputeSeries();
                Status = WorkflowStatus.Ready;
                ResetZoomToFit();
                RaiseGraphUpdated();
                return;
            }

            await RunFrameAnalysisAsync(probe, stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis pipeline error for stream {Index}", stream.Index);
            Status = WorkflowStatus.FrameAnalysisFailed;
            ErrorMessage = $"Analysis error: {ex.Message}";
        }
    }

    private async Task RunFrameAnalysisAsync(ProbedMediaFile probe, VideoStreamInfo stream)
    {
        Status = WorkflowStatus.RunningFrameAnalysis;
        LoadingPanel.IsVisible = true;
        LoadingPanel.Operation = $"Analyzing stream #{stream.Index} ({stream.CodecName})";
        var totalDuration = stream.Duration?.TotalSeconds ?? probe.Duration?.TotalSeconds;
        LoadingPanel.Progress = new AnalysisProgress(0, 0, totalDuration);

        var cts = new CancellationTokenSource();
        _analysisCts = cts;
        var observer = new FrameProgressObserver(this, totalDuration);

        try
        {
            var result = await _pipeline.RunFrameAnalysisAsync(probe, stream, observer, cts.Token);

            switch (result.Outcome)
            {
                case FrameAnalysisOutcome.Completed:
                    Status = WorkflowStatus.Ready;
                    LoadingPanel.IsVisible = false;
                    // Recompute after Status flips so the per-second aggregator includes
                    // the final (possibly partial) trailing bin now that ingestion is done.
                    RecomputeSeries();
                    ResetZoomToFit();
                    RaiseGraphUpdated();
                    break;
                case FrameAnalysisOutcome.Canceled:
                    Status = WorkflowStatus.FrameAnalysisCanceled;
                    IsPartialData = _frames.Count > 0;
                    LoadingPanel.IsVisible = false;
                    RecomputeSeries();
                    RaiseGraphUpdated();
                    break;
                case FrameAnalysisOutcome.Failed:
                    Status = WorkflowStatus.FrameAnalysisFailed;
                    ErrorMessage = $"Frame analysis failed: {result.ErrorMessage}";
                    LoadingPanel.IsVisible = false;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(result.Outcome), result.Outcome, "Unknown outcome");
            }
        }
        finally
        {
            if (_analysisCts == cts)
            {
                _analysisCts = null;
            }
            cts.Dispose();
        }
    }

    private sealed class FrameProgressObserver(MainWindowViewModel vm, double? totalDuration) : IFrameAnalysisProgressObserver
    {
        private long _lastFlushTimestamp = vm._timeProvider.GetTimestamp();

        public ValueTask OnFrameAsync(FrameRecord frame, long totalFrames, CancellationToken cancellationToken)
        {
            vm._frames.Add(frame);
            vm._sortedFrames = null;

            var now = vm._timeProvider.GetTimestamp();
            var elapsed = vm._timeProvider.GetElapsedTime(_lastFlushTimestamp, now);
            if (elapsed.TotalMilliseconds >= 100 || (totalFrames & 1023) == 0)
            {
                vm.LoadingPanel.Progress = new AnalysisProgress(totalFrames, frame.TimestampSeconds, totalDuration);
                vm.RecomputeSeries();
                vm.RaiseGraphUpdated();
                _lastFlushTimestamp = now;
            }
            return ValueTask.CompletedTask;
        }
    }

    [RelayCommand]
    private void CancelAnalysis()
    {
        CancelAnalysisInternal();
    }

    private void CancelAnalysisInternal()
    {
        var cts = _analysisCts;
        if (cts is not null && !cts.IsCancellationRequested)
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* already disposed */ }
        }
    }

    [RelayCommand(CanExecute = nameof(CanReload))]
    private async Task ReloadAsync()
    {
        if (FilePath is null) return;
        var path = FilePath;
        FilePath = null;
        ProbedFile = null;
        VideoStreams.Clear();
        SelectedStream = null;
        StreamMetadata.File = null;
        StreamMetadata.Stream = null;
        await LoadFileAsync(path);
    }

    private bool CanReload() => FilePath is not null;

    [RelayCommand(CanExecute = nameof(CanClearCurrentFileCache))]
    private async Task ClearCurrentFileCacheAsync()
    {
        if (ProbedFile is null || FilePath is null) return;
        await _pipeline.ClearFileCacheAsync(ProbedFile.Fingerprint);
        await ReloadAsync();
    }

    private bool CanClearCurrentFileCache() => ProbedFile is not null;

    [RelayCommand]
    private void ResetZoom()
    {
        ResetZoomToFit();
    }

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private void InstallUpdate()
    {
        _appUpdateService.ApplyPendingUpdateAndRestart();
    }

    [RelayCommand(CanExecute = nameof(CanShowTestUpdateNotification))]
    private void ShowTestUpdateNotification()
    {
        _appUpdateService.ShowTestUpdateNotification();
    }

    [RelayCommand(CanExecute = nameof(CanYZoomIn))]
    private void YZoomIn()
    {
        _yZoomLevel *= 2.0 / 3.0;
        if (_yZoomLevel < MinYZoom) _yZoomLevel = MinYZoom;
        YZoomChanged?.Invoke(this, _yZoomLevel);
        YZoomInCommand.NotifyCanExecuteChanged();
        YZoomOutCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanYZoomOut))]
    private void YZoomOut()
    {
        _yZoomLevel = Math.Min(1.0, _yZoomLevel * 3.0 / 2.0);
        YZoomChanged?.Invoke(this, _yZoomLevel);
        YZoomInCommand.NotifyCanExecuteChanged();
        YZoomOutCommand.NotifyCanExecuteChanged();
    }

    private bool CanYZoomIn() => _yZoomLevel > MinYZoom && _series.Count > 0;
    private bool CanYZoomOut() => _yZoomLevel < 1.0 && _series.Count > 0;
    private bool CanInstallUpdate() => _appUpdateService.IsUpdateReadyToInstall;
    private bool CanShowTestUpdateNotification() => _appRuntimeInfo.IsDevelopmentEnvironment;

    private void ResetZoomToFit()
    {
        _yZoomLevel = 1.0;
        if (_series.Count == 0)
        {
            VisibleRange = default;
            RequestResetZoom?.Invoke(this, default);
            return;
        }
        var range = new VisibleTimeRange(_series[0].TimeSeconds, _series[^1].TimeSeconds);
        VisibleRange = range;
        RequestResetZoom?.Invoke(this, range);
        RecomputeStatistics();
        YZoomInCommand.NotifyCanExecuteChanged();
        YZoomOutCommand.NotifyCanExecuteChanged();
    }

    public void UpdateVisibleRange(VisibleTimeRange newRange)
    {
        VisibleRange = newRange;
        RecomputeStatistics();
    }

    public void OnCursorMoved(double timeSeconds)
    {
        if (_series.Count == 0)
        {
            CursorReadout.Readout = Models.CursorReadout.Empty(GraphMode);
            return;
        }
        CursorReadout.Readout = CursorReadoutCalculator.AtTime(_series, timeSeconds, GraphMode);
    }

    partial void OnGraphModeChanged(GraphMode value)
    {
        OnPropertyChanged(nameof(HasWindowSetting));
        RecomputeSeries();
        RaiseGraphUpdated();
        PersistPreferences();
    }

    partial void OnRollingWindowChanged(RollingAverageWindow value)
    {
        if (HasWindowSetting)
        {
            RecomputeSeries();
            RaiseGraphUpdated();
        }
        PersistPreferences();
    }

    partial void OnThemeModeChanged(ThemeMode value)
    {
        _themeService.Apply(value);
        OnPropertyChanged(nameof(IsThemeSystem));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeDark));
        PersistPreferences();
    }

    partial void OnStatusChanged(WorkflowStatus value)
    {
        OnPropertyChanged(nameof(ShowFfprobeMissingError));
        OnPropertyChanged(nameof(ShowGenericError));
        OnPropertyChanged(nameof(ShowIdleHint));
    }

    partial void OnProbedFileChanged(ProbedMediaFile? value)
    {
        OnPropertyChanged(nameof(HasFile));
        ReloadCommand.NotifyCanExecuteChanged();
        ClearCurrentFileCacheCommand.NotifyCanExecuteChanged();
    }

    private void PersistPreferences()
    {
        // Fire and forget by design: preference changes come from sync property setters
        // and persistence failures only emit a log warning.
        _ = SafePersistPreferencesAsync();
    }

    private async Task SafePersistPreferencesAsync()
    {
        try
        {
            await _preferencesStore.UpdateAsync(
                current => current with
                {
                    Theme = ThemeMode,
                    GraphMode = GraphMode,
                    RollingWindow = RollingWindow
                }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Persisting preferences failed");
        }
    }

    private void ResetSeries()
    {
        _frames.Clear();
        _sortedFrames = null;
        _series = Array.Empty<BitratePoint>();
        _yZoomLevel = 1.0;
        SeriesVersion++;
        KnownDurationSeconds = null;
        Statistics.Statistics = VisibleRangeStatistics.Empty;
        CursorReadout.Readout = Models.CursorReadout.Empty(GraphMode);
        LoadingPanel.IsVisible = false;
        LoadingPanel.Progress = default;
        YZoomInCommand.NotifyCanExecuteChanged();
        YZoomOutCommand.NotifyCanExecuteChanged();
    }

    private IReadOnlyList<FrameRecord> GetSortedFrames()
    {
        if (_sortedFrames is not null) return _sortedFrames;

        // ffprobe emits frames in decode order, but the x-axis is presentation time.
        // Codecs with B-frames produce non-monotonic timestamps in decode order, so
        // we must sort before projecting or the scatter line zig-zags and the
        // duration-delta fallback is invalidated by out-of-order neighbors.
        _sortedFrames = _frames.Count == 0
            ? Array.Empty<FrameRecord>()
            : _frames.OrderBy(f => f.TimestampSeconds).ToArray();

        return _sortedFrames;
    }

    private void RecomputeSeries()
    {
        var ordered = GetSortedFrames();

        var analysisInFlight = Status == WorkflowStatus.RunningFrameAnalysis;
        _series = GraphMode switch
        {
            GraphMode.RollingAverage => RollingAverageCalculator.Compute(ordered, RollingWindow.ToSeconds()),
            GraphMode.PeakEnvelope => PeakEnvelopeCalculator.Compute(ordered, RollingWindow.ToSeconds(), excludeTrailingBin: analysisInFlight),
            GraphMode.PerFrame => FrameSeriesProjector.ProjectPerFrame(ordered),
            GraphMode.PerSecond => PerSecondAggregator.Aggregate(ordered, excludeTrailingBin: analysisInFlight),
            _ => throw new ArgumentOutOfRangeException(nameof(GraphMode), GraphMode, "Unknown graph mode")
        };
        SeriesVersion++;
        RecomputeStatistics();
    }

    private void RecomputeStatistics()
    {
        if (_series.Count == 0)
        {
            Statistics.Statistics = VisibleRangeStatistics.Empty;
            return;
        }
        var range = VisibleRange.IsValid
            ? VisibleRange
            : new VisibleTimeRange(_series[0].TimeSeconds, _series[^1].TimeSeconds);
        Statistics.Statistics = VisibleRangeStatisticsCalculator.Compute(_series, range);
    }

    private void RaiseGraphUpdated()
    {
        GraphSeriesUpdated?.Invoke(this, EventArgs.Empty);
        YZoomInCommand.NotifyCanExecuteChanged();
        YZoomOutCommand.NotifyCanExecuteChanged();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _appUpdateService.StateChanged -= OnAppUpdateStateChanged;
        CancelAnalysisInternal();
        return ValueTask.CompletedTask;
    }

    private void OnAppUpdateStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsUpdateReadyToInstall));
        OnPropertyChanged(nameof(UpdateButtonText));
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }
}
