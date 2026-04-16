using MediaBitrateViewer.App.Services;
using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.Analysis;
using MediaBitrateViewer.Core.ViewModels;
using MediaBitrateViewer.Infrastructure.Cache;
using MediaBitrateViewer.Infrastructure.Ffprobe;
using MediaBitrateViewer.Infrastructure.Files;
using MediaBitrateViewer.Infrastructure.Preferences;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaBitrateViewer.App.DependencyInjection;

public static class ServiceConfiguration
{
    public const string AppDirectoryName = "MediaBitrateViewer";

    public static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder
                .SetMinimumLevel(LogLevel.Information)
                .AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.IncludeScopes = false;
                })
                .AddDebug();
        });

        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<IFfprobeLocator, FfprobeLocator>();
        services.AddSingleton<IFileFingerprintService, FileFingerprintService>();
        services.AddSingleton<IFileProbeService, FileProbeService>();
        services.AddSingleton<IFrameAnalysisService, FrameAnalysisService>();
        services.AddSingleton<IAnalysisPipelineService, AnalysisPipelineService>();

        var cacheRoot = Path.Combine(Path.GetTempPath(), AppDirectoryName, "cache");
        services.AddSingleton<IAnalysisCache>(sp => new AnalysisCache(
            cacheRoot,
            sp.GetRequiredService<ILogger<AnalysisCache>>()));

        var prefsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDirectoryName,
            "preferences.json");
        services.AddSingleton<IUserPreferencesStore>(sp => new UserPreferencesStore(
            prefsPath,
            sp.GetRequiredService<ILogger<UserPreferencesStore>>()));

        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IWindowCoordinator, WindowCoordinator>();
        services.AddSingleton<IRecentFilesService, RecentFilesService>();

        services.AddScoped<CursorReadoutViewModel>();
        services.AddScoped<StatisticsPanelViewModel>();
        services.AddScoped<StreamMetadataViewModel>();
        services.AddScoped<LoadingPanelViewModel>();
        services.AddScoped<MainWindowViewModel>();
    }
}
