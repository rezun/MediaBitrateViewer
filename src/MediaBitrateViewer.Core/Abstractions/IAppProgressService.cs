namespace MediaBitrateViewer.Core.Abstractions;

/// <summary>
/// Cross-platform OS-level progress surface: Windows taskbar button, macOS Dock tile
/// badge, or Linux Unity launcher entry. Implementations are best-effort and silently
/// no-op on unsupported platforms or when the underlying desktop environment does not
/// expose the feature (e.g. non-Unity Linux desktops).
/// </summary>
public interface IAppProgressService
{
    /// <summary>
    /// Sets the visible progress fraction. Values outside [0, 1] are clamped.
    /// </summary>
    void SetProgress(double fraction);

    /// <summary>
    /// Removes any progress indicator and returns the icon to its normal state.
    /// </summary>
    void Clear();
}
