using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Core.Abstractions;

public interface IThemeService
{
    ThemeMode CurrentMode { get; }
    AppliedTheme AppliedTheme { get; }
    event EventHandler<AppliedTheme>? AppliedThemeChanged;
    void Apply(ThemeMode mode);
}

public enum AppliedTheme
{
    Light,
    Dark
}
