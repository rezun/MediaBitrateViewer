using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MediaBitrateViewer.App.Views;
using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.Models;
using MediaBitrateViewer.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaBitrateViewer.App.Services;

public sealed class WindowCoordinator : IWindowCoordinator
{
    private readonly IServiceProvider _provider;
    private readonly IUserPreferencesStore _preferencesStore;
    private readonly IThemeService _themeService;
    private readonly ILogger<WindowCoordinator> _logger;

    public WindowCoordinator(IServiceProvider provider, IUserPreferencesStore preferencesStore, IThemeService themeService, ILogger<WindowCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(preferencesStore);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(logger);
        _provider = provider;
        _preferencesStore = preferencesStore;
        _themeService = themeService;
        _logger = logger;
    }

    public void OpenInitialWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        // Load preferences synchronously — the file is tiny and placement AND
        // theme MUST be applied before the window is shown. Avalonia's desktop
        // lifetime auto-calls MainWindow.Show() after OnFrameworkInitializationCompleted
        // returns, so any async work runs too late and produces a visible jump
        // from XAML defaults to the restored geometry / from the OS theme to
        // the saved theme.
        var prefs = _preferencesStore.Load();
        _themeService.Apply(prefs.Theme);

        var (window, vm) = CreateWindow();
        ApplyPlacement(window, prefs.WindowPlacement);
        AttachPlacementSaver(window);

        desktop.MainWindow = window;
        window.Show();
        InitializeAsync(vm);
    }

    private void InitializeAsync(MainWindowViewModel vm)
    {
        // Fire-and-forget UI bootstrap: triggered from a sync UI lifecycle hook.
        // Wrapped in try/catch so async exceptions surface in logs.
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                await vm.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Window initialization failed");
            }
        });
    }

    public void OpenWindowFor(string filePath)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var prefs = _preferencesStore.Load();
            var (window, vm) = CreateWindow();
            ApplyPlacement(window, prefs.WindowPlacement);
            CascadeFromExistingWindows(window);
            window.Show();
            InitializeAndLoadAsync(vm, filePath);
        });
    }

    private static void CascadeFromExistingWindows(Window window)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        // Offset the new window from the most recently opened existing window so
        // cascading windows don't stack exactly on top of each other.
        var anchor = desktop.Windows.OfType<MainWindow>().LastOrDefault(w => !ReferenceEquals(w, window));
        if (anchor is null)
            return;

        const int offset = 30;
        var candidate = new PixelPoint(anchor.Position.X + offset, anchor.Position.Y + offset);
        if (!IsPositionOnAnyScreen(window, candidate, window.Width, window.Height))
            return;

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.WindowState = WindowState.Normal;
        window.Position = candidate;
    }

    public bool TryLoadInActiveEmptyWindow(string filePath)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return false;

        var active = desktop.Windows.OfType<MainWindow>().FirstOrDefault(w => w.IsActive)
                     ?? desktop.MainWindow as MainWindow;
        if (active?.DataContext is MainWindowViewModel vm && !vm.HasFile)
        {
            LoadAsync(vm, filePath);
            return true;
        }
        return false;
    }

    private (MainWindow window, MainWindowViewModel vm) CreateWindow()
    {
        var scope = _provider.CreateAsyncScope();
        var vm = scope.ServiceProvider.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = vm };

        window.Closed += (_, _) =>
        {
            // Avalonia's Closed event is a sync callback; this is the one acceptable
            // fire-and-forget site (bridging to async work).
            _ = DisposeScopeAsync(scope);
        };
        return (window, vm);
    }

    private void AttachPlacementSaver(Window window)
    {
        // Only the primary (first-created) window persists its placement. Secondary
        // windows opened via OpenWindowFor must not save, otherwise Cmd+Q shutdown
        // closes both windows and the secondary's bounds overwrite the primary's.
        WindowPlacement? capturedPlacement = null;

        // Capture in Closing, not Closed: on macOS the window is hidden by the time
        // Closed fires and Position reads as (0,0). Closing fires while the native
        // window is still alive, so coordinates and state are valid.
        window.Closing += (_, _) => capturedPlacement = CapturePlacement(window);

        window.Closed += (_, _) =>
        {
            if (capturedPlacement is not null)
                _ = SaveWindowPlacementAsync(capturedPlacement);
        };
    }

    private async Task DisposeScopeAsync(AsyncServiceScope scope)
    {
        try
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while disposing window scope");
        }
    }

    private async Task SaveWindowPlacementAsync(WindowPlacement placement)
    {
        try
        {
            await _preferencesStore.UpdateAsync(
                current => current with { WindowPlacement = placement },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while saving window placement on close");
        }
    }

    private static WindowPlacement CapturePlacement(Window window)
    {
        var state = window.WindowState == WindowState.Maximized
            ? WindowStateKind.Maximized
            : WindowStateKind.Normal;

        return new WindowPlacement
        {
            X = window.Position.X,
            Y = window.Position.Y,
            Width = window.Width,
            Height = window.Height,
            State = state
        };
    }

    private static void ApplyPlacement(Window window, WindowPlacement? placement)
    {
        if (placement is null)
            return;

        if (placement.Width > 0)
            window.Width = placement.Width;
        if (placement.Height > 0)
            window.Height = placement.Height;

        var position = new PixelPoint(placement.X, placement.Y);
        if (IsPositionOnAnyScreen(window, position, placement.Width, placement.Height))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = position;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (placement.State == WindowStateKind.Maximized)
            window.WindowState = WindowState.Maximized;
    }

    private static bool IsPositionOnAnyScreen(Window window, PixelPoint position, double width, double height)
    {
        var screens = window.Screens;
        if (screens is null || screens.All.Count == 0)
            return true;

        // Require the window's title-bar area to be visible — at least 200px wide
        // and the top edge must fall within a screen's working area.
        var probeWidth = (int)Math.Max(1, Math.Min(width, 200));
        var probe = new PixelRect(position.X, position.Y, probeWidth, 1);

        foreach (var screen in screens.All)
        {
            if (screen.WorkingArea.Intersects(probe))
                return true;
        }
        return false;
    }

    private void InitializeAndLoadAsync(MainWindowViewModel vm, string filePath)
    {
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                await vm.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
                await vm.LoadFileAsync(filePath).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Window initialization or file load failed for {Path}", filePath);
            }
        });
    }

    private void LoadAsync(MainWindowViewModel vm, string filePath)
    {
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                await vm.LoadFileAsync(filePath).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Loading file failed for {Path}", filePath);
            }
        });
    }
}
