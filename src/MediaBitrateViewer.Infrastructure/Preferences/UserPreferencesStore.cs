using System.Text.Json;
using MediaBitrateViewer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MediaBitrateViewer.Infrastructure.Preferences;

public sealed class UserPreferencesStore : IUserPreferencesStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly ILogger<UserPreferencesStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UserPreferencesStore(string filePath, ILogger<UserPreferencesStore> logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = filePath;
        _logger = logger;
    }

    public UserPreferences Load()
    {
        _gate.Wait();
        try
        {
            return LoadCore();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<UserPreferences> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SaveAsync(UserPreferences preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveCoreAsync(preferences, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<UserPreferences> UpdateAsync(
        Func<UserPreferences, UserPreferences> mutate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            var updated = mutate(current);
            await SaveCoreAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private UserPreferences LoadCore()
    {
        try
        {
            if (!File.Exists(_filePath))
                return UserPreferences.Default;

            using var stream = File.OpenRead(_filePath);
            return JsonSerializer.Deserialize<UserPreferences>(stream, JsonOptions) ?? UserPreferences.Default;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Could not load preferences from {Path}; using defaults", _filePath);
            return UserPreferences.Default;
        }
    }

    private async ValueTask<UserPreferences> LoadCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_filePath))
                return UserPreferences.Default;

            await using var stream = File.OpenRead(_filePath);
            var prefs = await JsonSerializer.DeserializeAsync<UserPreferences>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return prefs ?? UserPreferences.Default;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Could not load preferences from {Path}; using defaults", _filePath);
            return UserPreferences.Default;
        }
    }

    private async ValueTask SaveCoreAsync(UserPreferences preferences, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var tmp = _filePath + ".tmp";
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, preferences, JsonOptions, cancellationToken).ConfigureAwait(false);
            }
            File.Move(tmp, _filePath, overwrite: true);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not write preferences to {Path}", _filePath);
        }
    }

    public void Dispose() => _gate.Dispose();
}
