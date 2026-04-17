using System.Diagnostics;
using System.Globalization;
using MediaBitrateViewer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MediaBitrateViewer.App.Services.Progress;

/// <summary>
/// Linux implementation: emits a com.canonical.Unity.LauncherEntry.Update DBus
/// signal via the `gdbus` CLI (part of glib2, near-universal on Linux desktops).
/// Shells that honor the protocol — classic Unity, modern Plank, GNOME Dash with
/// the dash-to-dock extension — display a progress bar on the launcher icon.
/// Environments that do not listen for the signal silently ignore it.
/// </summary>
/// <remarks>
/// Shelling out to gdbus avoids pulling a DBus NuGet dependency for a Linux-only
/// feature. The process launch is throttled by the caller's frame-flush cadence
/// and skipped when the percent integer is unchanged.
/// </remarks>
public sealed class LinuxUnityLauncherProgressService : IAppProgressService
{
    private const string DesktopFileId = "mediabitrateviewer.desktop";
    private const string ObjectPath = "/com/canonical/Unity/LauncherEntry";
    private const string SignalName = "com.canonical.Unity.LauncherEntry.Update";

    private readonly ILogger<LinuxUnityLauncherProgressService> _logger;
    private bool _gdbusMissing;
    private int _lastPercent = -1;
    private bool _lastVisible;

    public LinuxUnityLauncherProgressService(ILogger<LinuxUnityLauncherProgressService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public void SetProgress(double fraction)
    {
        var clamped = double.IsFinite(fraction) ? Math.Clamp(fraction, 0.0, 1.0) : 0.0;
        var percent = (int)Math.Round(clamped * 100.0, MidpointRounding.AwayFromZero);
        if (percent == _lastPercent && _lastVisible) return;

        _lastPercent = percent;
        _lastVisible = true;

        var payload = string.Create(
            CultureInfo.InvariantCulture,
            $"{{'progress': <{clamped:0.0000}>, 'progress-visible': <true>}}");
        Emit(payload);
    }

    public void Clear()
    {
        if (!_lastVisible) return;
        _lastVisible = false;
        _lastPercent = -1;
        Emit("{'progress-visible': <false>}");
    }

    private void Emit(string dictBody)
    {
        if (_gdbusMissing) return;

        var args = new[]
        {
            "emit",
            "--session",
            "--object-path", ObjectPath,
            "--signal", SignalName,
            $"application://{DesktopFileId}",
            dictBody
        };

        var psi = new ProcessStartInfo("gdbus")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi);
            // gdbus emit exits immediately — we don't wait to avoid blocking the UI
            // thread. Any failure surfaces as a non-zero exit but won't be observed
            // here; it's a best-effort, fire-and-forget signal.
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _gdbusMissing = true;
            _logger.LogDebug(ex, "gdbus not available; launcher progress disabled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Emitting Unity launcher progress failed");
        }
    }
}
