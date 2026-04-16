using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Tests.Fakes;

internal sealed class FakeFfprobeLocator(bool available) : IFfprobeLocator
{
    public ValueTask<FfprobeLocation> LocateAsync(CancellationToken cancellationToken) =>
        new(new FfprobeLocation(available ? "ffprobe" : null, available, available ? "ffprobe-fake" : null));
}

internal sealed class FakeAppUpdateService : IAppUpdateService
{
    private string _buttonText = string.Empty;

    public event EventHandler? StateChanged;

    public bool IsUpdateReadyToInstall { get; private set; }
    public string UpdateButtonText => _buttonText;
    public int StartCalls { get; private set; }
    public int ApplyCalls { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        StartCalls++;
        return Task.CompletedTask;
    }

    public void ApplyPendingUpdateAndRestart()
    {
        ApplyCalls++;
    }

    public void ShowTestUpdateNotification()
    {
        SetPendingUpdate("9.9.9-debug");
    }

    public void SetPendingUpdate(string? version)
    {
        IsUpdateReadyToInstall = !string.IsNullOrWhiteSpace(version);
        _buttonText = IsUpdateReadyToInstall ? $"Restart to install {version}" : string.Empty;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class FakeAppRuntimeInfo : IAppRuntimeInfo
{
    public bool IsDevelopmentEnvironment { get; set; }
}

internal sealed class FakeAppVersionProvider : IAppVersionProvider
{
    public string DisplayVersion { get; set; } = "1.2.3-test";
}

internal sealed class FakeFingerprintService : IFileFingerprintService
{
    public ValueTask<FileFingerprint> ComputeAsync(string filePath, CancellationToken cancellationToken) =>
        new(new FileFingerprint(1024, DateTimeOffset.UnixEpoch, "AA", "BB"));
}

internal sealed class FakeProbeService : IFileProbeService
{
    public int Calls { get; private set; }

    public ValueTask<ProbedMediaFile> ProbeAsync(string filePath, FileFingerprint fingerprint, CancellationToken cancellationToken)
    {
        Calls++;
        var probe = new ProbedMediaFile
        {
            FilePath = filePath,
            Fingerprint = fingerprint,
            FormatName = "mp4",
            FormatLongName = "MPEG-4",
            Duration = TimeSpan.FromSeconds(60),
            VideoStreams = new[]
            {
                new VideoStreamInfo
                {
                    Index = 0,
                    CodecName = "h264",
                    Width = 1280,
                    Height = 720,
                    Duration = TimeSpan.FromSeconds(60)
                },
                new VideoStreamInfo
                {
                    Index = 1,
                    CodecName = "hevc",
                    Width = 1920,
                    Height = 1080,
                    Duration = TimeSpan.FromSeconds(60)
                }
            }
        };
        return new ValueTask<ProbedMediaFile>(probe);
    }
}

internal sealed class FakeFrameAnalysisService : IFrameAnalysisService
{
    public int Calls { get; private set; }
    public int FramesToEmit { get; set; } = 5;

    /// <summary>
    /// Signaled after the first frame is emitted so tests can synchronize on analysis
    /// actually being in-flight rather than merely scheduled.
    /// </summary>
    public TaskCompletionSource FirstFrameEmitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// When set, the enumerator awaits this task between frames. Tests use it to hold
    /// analysis open long enough to observe mid-run state (cancellation, partial data).
    /// </summary>
    public Task? HoldAfterFirstFrame { get; set; }

    public async IAsyncEnumerable<FrameRecord> AnalyzeAsync(
        string filePath, int videoStreamIndex,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Calls++;
        for (var i = 0; i < FramesToEmit; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new FrameRecord
            {
                TimestampSeconds = i * 0.04,
                DurationSeconds = 0.04,
                PacketSizeBytes = 1000 + i * 100
            };
            if (i == 0)
            {
                FirstFrameEmitted.TrySetResult();
                if (HoldAfterFirstFrame is not null)
                {
                    await HoldAfterFirstFrame.WaitAsync(cancellationToken);
                }
            }
            await Task.Yield();
        }
    }
}

internal sealed class FakeCache : IAnalysisCache
{
    private readonly Dictionary<string, ProbedMediaFile> _probes = new();
    private readonly Dictionary<string, CachedFrameAnalysis> _frames = new();

    public int ClearFileCalls { get; private set; }

    private static string ProbeKey(FileFingerprint fp) => fp.ToCacheKey();
    private static string FramesKey(FileFingerprint fp, int idx) => $"{fp.ToCacheKey()}|{idx}";

    public ValueTask<ProbedMediaFile?> TryGetProbeAsync(FileFingerprint fingerprint, CancellationToken cancellationToken)
        => new(_probes.TryGetValue(ProbeKey(fingerprint), out var v) ? v : null);

    public ValueTask SaveProbeAsync(ProbedMediaFile probe, CancellationToken cancellationToken)
    {
        _probes[ProbeKey(probe.Fingerprint)] = probe;
        return ValueTask.CompletedTask;
    }

    public ValueTask<CachedFrameAnalysis?> TryGetCompleteFrameAnalysisAsync(FileFingerprint fingerprint, int videoStreamIndex, CancellationToken cancellationToken)
        => new(_frames.TryGetValue(FramesKey(fingerprint, videoStreamIndex), out var v) ? v : null);

    public IFrameCacheWriter BeginFrameAnalysis(FileFingerprint fingerprint, int videoStreamIndex, TimeProvider timeProvider)
        => new Writer(this, fingerprint, videoStreamIndex);

    public void SeedCompletedFrameAnalysis(FileFingerprint fingerprint, int videoStreamIndex, IReadOnlyList<FrameRecord> frames)
        => _frames[FramesKey(fingerprint, videoStreamIndex)] = new CachedFrameAnalysis(fingerprint, videoStreamIndex, frames, DateTimeOffset.UnixEpoch);

    public ValueTask ClearFileAsync(FileFingerprint fingerprint)
    {
        ClearFileCalls++;
        _probes.Remove(ProbeKey(fingerprint));
        var prefix = fingerprint.ToCacheKey() + "|";
        foreach (var k in _frames.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            _frames.Remove(k);
        return ValueTask.CompletedTask;
    }

    private sealed class Writer(FakeCache parent, FileFingerprint fp, int idx) : IFrameCacheWriter
    {
        private readonly List<FrameRecord> _frames = new();
        public ValueTask AppendAsync(FrameRecord frame, CancellationToken cancellationToken)
        {
            _frames.Add(frame);
            return ValueTask.CompletedTask;
        }
        public ValueTask MarkCompleteAsync(CancellationToken cancellationToken)
        {
            parent._frames[FramesKey(fp, idx)] = new CachedFrameAnalysis(fp, idx, _frames.ToArray(), DateTimeOffset.UtcNow);
            return ValueTask.CompletedTask;
        }
        public ValueTask MarkCanceledAsync() => ValueTask.CompletedTask;
        public ValueTask MarkFailedAsync(string errorSummary) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class FakePrefsStore : IUserPreferencesStore
{
    private UserPreferences _current = UserPreferences.Default;

    public int UpdateCalls { get; private set; }
    public UserPreferences Current => _current;

    public void Seed(UserPreferences prefs) => _current = prefs;

    public UserPreferences Load() => _current;
    public ValueTask<UserPreferences> LoadAsync(CancellationToken cancellationToken) => new(_current);
    public ValueTask SaveAsync(UserPreferences preferences, CancellationToken cancellationToken)
    {
        _current = preferences;
        return ValueTask.CompletedTask;
    }
    public ValueTask<UserPreferences> UpdateAsync(Func<UserPreferences, UserPreferences> mutate, CancellationToken cancellationToken)
    {
        UpdateCalls++;
        _current = mutate(_current);
        return new(_current);
    }
}

internal sealed class FakeThemeService : IThemeService
{
    public ThemeMode CurrentMode { get; private set; } = ThemeMode.System;
    public AppliedTheme AppliedTheme { get; private set; } = AppliedTheme.Light;
    public event EventHandler<AppliedTheme>? AppliedThemeChanged;
    public void Apply(ThemeMode mode)
    {
        CurrentMode = mode;
        var newApplied = mode == ThemeMode.Dark ? AppliedTheme.Dark : AppliedTheme.Light;
        if (newApplied != AppliedTheme)
        {
            AppliedTheme = newApplied;
            AppliedThemeChanged?.Invoke(this, newApplied);
        }
    }
}

internal sealed class FakeUiDispatcher : IUiDispatcher
{
    public int InvokeAsyncCalls { get; private set; }

    public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        InvokeAsyncCalls++;
        action();
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeFilePicker : IFilePickerService
{
    public string? Next { get; set; }
    public ValueTask<string?> PickVideoFileAsync(CancellationToken cancellationToken) => new(Next);
}

internal sealed class FakeRecentFilesService : IRecentFilesService
{
    private readonly ObservableCollection<string> _files = new();
    public ReadOnlyObservableCollection<string> Files { get; }

    public FakeRecentFilesService()
    {
        Files = new ReadOnlyObservableCollection<string>(_files);
    }

    public Task AddAsync(string filePath)
    {
        for (var i = _files.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_files[i], filePath, StringComparison.Ordinal))
                _files.RemoveAt(i);
        }
        _files.Insert(0, filePath);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string filePath)
    {
        for (var i = _files.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_files[i], filePath, StringComparison.Ordinal))
                _files.RemoveAt(i);
        }
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        _files.Clear();
        return Task.CompletedTask;
    }
}

internal sealed class FakeWindowCoordinator : IWindowCoordinator
{
    public List<string> OpenedFiles { get; } = new();
    public bool InitialOpened { get; private set; }
    public void OpenInitialWindow() => InitialOpened = true;
    public void OpenWindowFor(string filePath) => OpenedFiles.Add(filePath);
    public bool TryLoadInActiveEmptyWindow(string filePath) => false;
}
