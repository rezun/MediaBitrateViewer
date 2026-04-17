using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MediaBitrateViewer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MediaBitrateViewer.App.Services.Progress;

/// <summary>
/// Windows implementation: drives the taskbar button progress overlay via
/// ITaskbarList3 (SetProgressValue / SetProgressState). The progress is applied to
/// every open top-level window — in normal single-window use this matches the
/// native feel, and multi-window analyses all light up together.
/// </summary>
public sealed class WindowsTaskbarProgressService : IAppProgressService, IDisposable
{
    private const ulong ProgressDenominator = 10_000UL;

    private readonly ILogger<WindowsTaskbarProgressService> _logger;
    private readonly object _gate = new();
    private ITaskbarList3? _taskbar;
    private bool _initFailed;
    private bool _disposed;

    public WindowsTaskbarProgressService(ILogger<WindowsTaskbarProgressService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public void SetProgress(double fraction)
    {
        var clamped = double.IsFinite(fraction) ? Math.Clamp(fraction, 0.0, 1.0) : 0.0;
        var completed = (ulong)Math.Round(clamped * ProgressDenominator, MidpointRounding.AwayFromZero);

        ApplyToAllWindows((taskbar, hwnd) =>
        {
            taskbar.SetProgressState(hwnd, TbpFlag.Normal);
            taskbar.SetProgressValue(hwnd, completed, ProgressDenominator);
        });
    }

    public void Clear()
    {
        ApplyToAllWindows((taskbar, hwnd) => taskbar.SetProgressState(hwnd, TbpFlag.NoProgress));
    }

    private void ApplyToAllWindows(Action<ITaskbarList3, IntPtr> action)
    {
        var taskbar = GetOrCreate();
        if (taskbar is null) return;

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        foreach (var window in desktop.Windows)
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) continue;

            try
            {
                action(taskbar, hwnd);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update taskbar progress for window handle {Hwnd}", hwnd);
            }
        }
    }

    private ITaskbarList3? GetOrCreate()
    {
        if (_initFailed || _disposed) return null;
        if (_taskbar is not null) return _taskbar;

        lock (_gate)
        {
            if (_taskbar is not null) return _taskbar;
            if (_initFailed) return null;

            try
            {
                var type = Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11D0-958A-006097C9A090"));
                if (type is null)
                {
                    _initFailed = true;
                    return null;
                }
                var instance = Activator.CreateInstance(type);
                if (instance is not ITaskbarList3 taskbar)
                {
                    _initFailed = true;
                    return null;
                }
                taskbar.HrInit();
                _taskbar = taskbar;
                return _taskbar;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Initializing ITaskbarList3 failed; taskbar progress disabled");
                _initFailed = true;
                return null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var taskbar = _taskbar;
        _taskbar = null;
        if (taskbar is not null)
        {
            Marshal.FinalReleaseComObject(taskbar);
        }
    }

    private enum TbpFlag : uint
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8
    }

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);

        // ITaskbarList3
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, TbpFlag state);
        // Remaining members (RegisterTab, UnregisterTab, SetTabOrder, SetTabActive,
        // ThumbBar*, SetOverlayIcon, SetThumbnailTooltip, SetThumbnailClip) are
        // intentionally omitted — the COM vtable contract is preserved by declaring
        // methods in order, and this interface instance is only used for progress.
    }
}
