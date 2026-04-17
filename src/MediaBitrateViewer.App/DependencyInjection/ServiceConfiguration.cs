using MediaBitrateViewer.App.Configuration;
using MediaBitrateViewer.App.Services;
using MediaBitrateViewer.App.Services.Progress;
using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.Analysis;
using MediaBitrateViewer.Core.ViewModels;
using MediaBitrateViewer.Infrastructure.Cache;
using MediaBitrateViewer.Infrastructure.Ffprobe;
using MediaBitrateViewer.Infrastructure.Files;
using MediaBitrateViewer.Infrastructure.Preferences;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaBitrateViewer.App.DependencyInjection;

public static class ServiceConfiguration
{
    public const string AppDirectoryName = "MediaBitrateViewer";

    public static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration();
        ConfigureServices(services, configuration);
        return services.BuildServiceProvider();
    }

    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

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

        services.AddSingleton(configuration);
        services
            .AddOptions<UpdateSettings>()
            .Bind(configuration.GetSection(UpdateSettings.SectionName));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAppRuntimeInfo, AppRuntimeInfo>();
        services.AddSingleton<IAppVersionProvider, AppVersionProvider>();

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
        services.AddSingleton<IUiDispatcher, UiDispatcher>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IWindowCoordinator, WindowCoordinator>();
        services.AddSingleton<IRecentFilesService, RecentFilesService>();
        services.AddSingleton<IAppUpdateService, VelopackUpdateService>();
        services.AddSingleton<IAppProgressService>(sp =>
        {
            if (OperatingSystem.IsWindows())
                return ActivatorUtilities.CreateInstance<WindowsTaskbarProgressService>(sp);
            if (OperatingSystem.IsMacOS())
                return new MacOsDockBadgeProgressService();
            if (OperatingSystem.IsLinux())
                return ActivatorUtilities.CreateInstance<LinuxUnityLauncherProgressService>(sp);
            return new NoOpAppProgressService();
        });

        services.AddScoped<CursorReadoutViewModel>();
        services.AddScoped<StatisticsPanelViewModel>();
        services.AddScoped<StreamMetadataViewModel>();
        services.AddScoped<LoadingPanelViewModel>();
        services.AddScoped<MainWindowViewModel>();
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();
    }
}
