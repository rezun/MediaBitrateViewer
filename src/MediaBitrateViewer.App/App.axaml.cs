using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MediaBitrateViewer.App.DependencyInjection;
using MediaBitrateViewer.App.Services;
using MediaBitrateViewer.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MediaBitrateViewer.App;

public partial class App : Application
{
    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services = ServiceConfiguration.BuildServiceProvider();

        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            activatable.Activated += OnAppActivated;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var coordinator = Services.GetRequiredService<IWindowCoordinator>();
            coordinator.OpenInitialWindow();

            // Windows/Linux deliver dropped-on-icon and Explorer-opened files as
            // process args. macOS uses IActivatableLifetime instead (see below),
            // but also surfaces argv via application:openFiles: — so the extension
            // filter in CollectVideoPaths is what keeps a `dotnet App.dll` dev
            // launch from being treated as a video open.
            var argPaths = CollectVideoPaths(desktop.Args ?? Array.Empty<string>());
            if (argPaths.Count > 0)
                OpenFiles(argPaths);

            desktop.ShutdownRequested += (_, _) =>
            {
                if (Services is IDisposable d) d.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnAppActivated(object? sender, ActivatedEventArgs e)
    {
        if (e is not FileActivatedEventArgs fileArgs) return;

        var paths = CollectVideoPaths(fileArgs.Files.Select(f => f.TryGetLocalPath()));
        if (paths.Count == 0) return;

        // Defer to the UI thread so this runs after OpenInitialWindow has created
        // the empty primary window (covers the cold-start case where the OS
        // launches the app to open a file).
        Dispatcher.UIThread.Post(() => OpenFiles(paths));
    }

    private static IReadOnlyList<string> CollectVideoPaths(IEnumerable<string?> candidates)
    {
        var paths = new List<string>();
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            if (!VideoFileExtensions.IsRecognized(candidate)) continue;
            if (!File.Exists(candidate)) continue;
            paths.Add(candidate);
        }
        return paths;
    }

    private void OpenFiles(IReadOnlyList<string> paths)
    {
        if (Services?.GetService<IWindowCoordinator>() is not { } coordinator) return;

        for (var i = 0; i < paths.Count; i++)
        {
            if (i == 0 && coordinator.TryLoadInActiveEmptyWindow(paths[i]))
                continue;
            coordinator.OpenWindowFor(paths[i]);
        }
    }
}
