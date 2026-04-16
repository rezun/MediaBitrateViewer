using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.Models;
using MediaBitrateViewer.Infrastructure.Preferences;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaBitrateViewer.Tests.Preferences;

public sealed class UserPreferencesStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mbv-prefs-tests-" + Guid.NewGuid().ToString("N"));

    public UserPreferencesStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Missing_ReturnsDefaults()
    {
        var path = Path.Combine(_dir, "prefs.json");
        var store = new UserPreferencesStore(path, NullLogger<UserPreferencesStore>.Instance);
        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.Equal(ThemeMode.System, loaded.Theme);
        Assert.Equal(GraphMode.PerSecond, loaded.GraphMode);
        Assert.Equal(RollingAverageWindow.Sec1, loaded.RollingWindow);
    }

    [Fact]
    public async Task Saved_RoundTripsAcrossInstances()
    {
        var path = Path.Combine(_dir, "prefs.json");
        var store = new UserPreferencesStore(path, NullLogger<UserPreferencesStore>.Instance);
        await store.SaveAsync(new UserPreferences
        {
            Theme = ThemeMode.Dark,
            GraphMode = GraphMode.RollingAverage,
            RollingWindow = RollingAverageWindow.Ms500
        }, CancellationToken.None);

        var second = new UserPreferencesStore(path, NullLogger<UserPreferencesStore>.Instance);
        var loaded = await second.LoadAsync(CancellationToken.None);

        Assert.Equal(ThemeMode.Dark, loaded.Theme);
        Assert.Equal(GraphMode.RollingAverage, loaded.GraphMode);
        Assert.Equal(RollingAverageWindow.Ms500, loaded.RollingWindow);
    }

    [Fact]
    public async Task UnreadableFile_ReturnsDefaults()
    {
        var path = Path.Combine(_dir, "prefs.json");
        File.WriteAllText(path, "{not valid json");
        var store = new UserPreferencesStore(path, NullLogger<UserPreferencesStore>.Instance);
        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.Equal(ThemeMode.System, loaded.Theme);
    }
}
