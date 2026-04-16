using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Core.Abstractions;

public interface IUserPreferencesStore
{
    UserPreferences Load();
    ValueTask<UserPreferences> LoadAsync(CancellationToken cancellationToken);
    ValueTask SaveAsync(UserPreferences preferences, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically loads, applies <paramref name="mutate"/>, and saves preferences.
    /// The entire read-modify-write runs under a single lock, preventing concurrent
    /// callers from overwriting each other's changes.
    /// </summary>
    ValueTask<UserPreferences> UpdateAsync(Func<UserPreferences, UserPreferences> mutate, CancellationToken cancellationToken);
}

public sealed record UserPreferences
{
    public ThemeMode Theme { get; init; } = ThemeMode.System;
    public GraphMode GraphMode { get; init; } = GraphMode.PerSecond;
    public RollingAverageWindow RollingWindow { get; init; } = RollingAverageWindow.Sec1;
    public WindowPlacement? WindowPlacement { get; init; }
    public IReadOnlyList<string> RecentFiles { get; init; } = Array.Empty<string>();

    public static UserPreferences Default { get; } = new();
}

public enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2
}
