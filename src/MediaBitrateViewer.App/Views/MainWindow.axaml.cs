using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaBitrateViewer.App.Views;

public partial class MainWindow : Window
{
    private NativeMenu? _recentFilesMenu;
    private INotifyCollectionChanged? _recentFilesSubscription;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _recentFilesMenu = FindRecentFilesSubmenu();
        RebuildRecentFilesMenu();

        if (DataContext is MainWindowViewModel vm && vm.RecentFiles is INotifyCollectionChanged notify)
        {
            notify.CollectionChanged += OnRecentFilesChanged;
            _recentFilesSubscription = notify;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_recentFilesSubscription is not null)
        {
            _recentFilesSubscription.CollectionChanged -= OnRecentFilesChanged;
            _recentFilesSubscription = null;
        }
        base.OnClosed(e);
    }

    private NativeMenu? FindRecentFilesSubmenu()
    {
        var root = NativeMenu.GetMenu(this);
        if (root is null) return null;

        foreach (var topLevel in root.Items.OfType<NativeMenuItem>())
        {
            if (!string.Equals(topLevel.Header, "File", StringComparison.Ordinal)) continue;
            var fileMenu = topLevel.Menu;
            if (fileMenu is null) continue;
            foreach (var item in fileMenu.Items.OfType<NativeMenuItem>())
            {
                if (string.Equals(item.Header, "Open Recent", StringComparison.Ordinal))
                    return item.Menu;
            }
        }
        return null;
    }

    private void OnRecentFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildRecentFilesMenu();
    }

    private void RebuildRecentFilesMenu()
    {
        if (_recentFilesMenu is null) return;
        if (DataContext is not MainWindowViewModel vm) return;

        _recentFilesMenu.Items.Clear();

        if (vm.RecentFiles.Count == 0)
        {
            _recentFilesMenu.Items.Add(new NativeMenuItem("(No recent files)") { IsEnabled = false });
            return;
        }

        foreach (var path in vm.RecentFiles)
        {
            _recentFilesMenu.Items.Add(new NativeMenuItem(Path.GetFileName(path))
            {
                Command = vm.OpenRecentCommand,
                CommandParameter = path
            });
        }

        _recentFilesMenu.Items.Add(new NativeMenuItemSeparator());
        _recentFilesMenu.Items.Add(new NativeMenuItem("Clear Recent Files")
        {
            Command = vm.ClearRecentCommand
        });
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Cmd+W close-window shortcut for macOS. Avalonia doesn't wire this up by
        // default (that normally comes from a NativeMenu). Windows and Linux get
        // Alt+F4 / WM-provided close handling at the OS level, so no extra branch
        // is needed here. Window.Close is a view concern so this stays in code-behind.
        if (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (HasFiles(e))
        {
            DropOverlay.IsVisible = true;
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        DropOverlay.IsVisible = false;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        // Avalonia drag-and-drop handlers must be `async void`; this is the bridge
        // to async work. Wrapped in try/catch so exceptions surface in logs.
        try
        {
            DropOverlay.IsVisible = false;

            if (DataContext is not MainWindowViewModel vm) return;
            if (!e.Data.Contains(DataFormats.Files)) return;

            var files = e.Data.GetFiles()?.ToArray();
            if (files is null || files.Length == 0) return;

            var paths = new List<string>();
            foreach (var item in files)
            {
                var local = item.TryGetLocalPath();
                if (string.IsNullOrEmpty(local)) continue;
                if (!File.Exists(local)) continue;
                paths.Add(local);
            }
            if (paths.Count == 0) return;

            // First file: route through OpenFromExternalAsync (loads here if empty,
            // otherwise opens new window). Remaining files always open new windows.
            await vm.OpenFromExternalAsync(paths[0]).ConfigureAwait(true);

            if (paths.Count > 1)
            {
                if (App.Current is App app && app.Services is not null)
                {
                    var coordinator = app.Services.GetRequiredService<IWindowCoordinator>();
                    for (var i = 1; i < paths.Count; i++)
                        coordinator.OpenWindowFor(paths[i]);
                }
            }
        }
        catch (Exception ex)
        {
            if (App.Current is App { Services: { } svc })
                svc.GetService<ILoggerFactory>()?.CreateLogger<MainWindow>().LogError(ex, "Drop handler failed");
        }
    }

    private static bool HasFiles(DragEventArgs e) => e.Data.Contains(DataFormats.Files);
}
