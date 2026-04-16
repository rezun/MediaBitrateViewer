using Avalonia.Threading;
using MediaBitrateViewer.App.Configuration;
using MediaBitrateViewer.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Velopack;
using Velopack.Sources;

namespace MediaBitrateViewer.App.Services;

public sealed class VelopackUpdateService : IAppUpdateService, IAsyncDisposable
{
    private static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromHours(4);
    private static readonly TimeSpan MinimumCheckInterval = TimeSpan.FromMinutes(15);

    private readonly UpdateSettings _settings;
    private readonly IAppRuntimeInfo _runtimeInfo;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VelopackUpdateService> _logger;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly Lock _startLock = new();

    private CancellationTokenSource? _lifetimeCts;
    private Task? _backgroundTask;
    private UpdateManager? _updateManager;
    private VelopackAsset? _pendingUpdate;
    private string? _testUpdateVersion;
    private bool _started;

    public event EventHandler? StateChanged;

    public bool IsUpdateReadyToInstall => _pendingUpdate is not null || _testUpdateVersion is not null;

    public string UpdateButtonText =>
        _pendingUpdate is not null
            ? $"Restart to install {_pendingUpdate.Version}"
            : _testUpdateVersion is not null
                ? $"Restart to install {_testUpdateVersion}"
                : string.Empty;

    public VelopackUpdateService(
        IOptions<UpdateSettings> settings,
        IAppRuntimeInfo runtimeInfo,
        TimeProvider timeProvider,
        ILogger<VelopackUpdateService> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(runtimeInfo);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings.Value;
        _runtimeInfo = runtimeInfo;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_startLock)
        {
            if (_started)
                return Task.CompletedTask;

            _started = true;
        }

        if (!_settings.Enabled)
        {
            _logger.LogInformation("Automatic updates are disabled by configuration.");
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(_settings.GithubRepositoryUrl))
        {
            _logger.LogWarning("Automatic updates are enabled, but no GitHub repository URL is configured.");
            return Task.CompletedTask;
        }

        try
        {
            _updateManager = new UpdateManager(new GithubSource(_settings.GithubRepositoryUrl, null, false, null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Velopack update manager.");
            return Task.CompletedTask;
        }

        if (_updateManager.CurrentVersion is null)
        {
            _logger.LogInformation("Velopack updates are unavailable for this run because the app is not running from an installed Velopack release.");
            return Task.CompletedTask;
        }

        RefreshPendingUpdateState();

        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _backgroundTask = RunUpdateLoopAsync(_lifetimeCts.Token);
        return Task.CompletedTask;
    }

    public void ApplyPendingUpdateAndRestart()
    {
        if (_pendingUpdate is null && _testUpdateVersion is not null)
        {
            _logger.LogInformation("Dismissing test update notification.");
            _testUpdateVersion = null;
            RaiseStateChanged();
            return;
        }

        if (_updateManager is null || _pendingUpdate is null)
            return;

        _logger.LogInformation("Applying downloaded update {Version} and restarting.", _pendingUpdate.Version);
        _updateManager.ApplyUpdatesAndRestart(_pendingUpdate);
    }

    public void ShowTestUpdateNotification()
    {
        if (!_runtimeInfo.IsDevelopmentEnvironment)
            return;

        if (_pendingUpdate is not null)
        {
            _logger.LogInformation("Ignoring test update notification request because a real downloaded update is already pending.");
            return;
        }

        _testUpdateVersion = "9.9.9-debug";
        RaiseStateChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (_lifetimeCts is not null)
        {
            await _lifetimeCts.CancelAsync().ConfigureAwait(false);
            _lifetimeCts.Dispose();
            _lifetimeCts = null;
        }

        if (_backgroundTask is not null)
        {
            try
            {
                await _backgroundTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }

            _backgroundTask = null;
        }

        _checkGate.Dispose();
    }

    private async Task RunUpdateLoopAsync(CancellationToken cancellationToken)
    {
        await CheckForUpdatesAsync(cancellationToken).ConfigureAwait(false);

        var interval = NormalizeCheckInterval(_settings.CheckInterval);
        using var timer = new PeriodicTimer(interval, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await CheckForUpdatesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
    }

    private async Task CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        if (_updateManager is null || _pendingUpdate is not null)
            return;

        if (!await _checkGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            _logger.LogInformation("Checking GitHub Releases for updates.");
            var update = await _updateManager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
                return;

            _logger.LogInformation("Update {Version} found. Downloading in the background.", update.TargetFullRelease.Version);
            await _updateManager.DownloadUpdatesAsync(update, cancelToken: cancellationToken).ConfigureAwait(false);
            RefreshPendingUpdateState();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Automatic update check failed.");
        }
        finally
        {
            _checkGate.Release();
        }
    }

    private void RefreshPendingUpdateState()
    {
        if (_updateManager is null)
            return;

        var pending = _updateManager.UpdatePendingRestart;
        var changed = !string.Equals(_pendingUpdate?.Version?.ToString(), pending?.Version?.ToString(), StringComparison.Ordinal);
        _pendingUpdate = pending;

        if (!changed)
            return;

        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        Dispatcher.UIThread.Post(() => StateChanged?.Invoke(this, EventArgs.Empty));
    }

    private TimeSpan NormalizeCheckInterval(TimeSpan configuredInterval)
    {
        if (configuredInterval >= MinimumCheckInterval)
            return configuredInterval;

        if (configuredInterval > TimeSpan.Zero)
        {
            _logger.LogWarning(
                "Configured update check interval {ConfiguredInterval} is below the minimum supported value of {MinimumInterval}. Using the minimum instead.",
                configuredInterval,
                MinimumCheckInterval);
        }
        else
        {
            _logger.LogWarning(
                "Configured update check interval {ConfiguredInterval} is invalid. Using the default interval of {DefaultInterval}.",
                configuredInterval,
                DefaultCheckInterval);

            return DefaultCheckInterval;
        }

        return MinimumCheckInterval;
    }
}
