using Avalonia;
using Avalonia.Styling;
using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.App.Services;

public sealed class ThemeService : IThemeService
{
    private ThemeMode _currentMode = ThemeMode.System;
    private AppliedTheme? _appliedTheme;

    public ThemeMode CurrentMode => _currentMode;
    public AppliedTheme AppliedTheme => _appliedTheme ?? AppliedTheme.Light;

    public event EventHandler<AppliedTheme>? AppliedThemeChanged;

    public void Apply(ThemeMode mode)
    {
        _currentMode = mode;
        var app = Application.Current;
        if (app is null) return;

        var variant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        app.RequestedThemeVariant = variant;

        var actual = mode switch
        {
            ThemeMode.Light => AppliedTheme.Light,
            ThemeMode.Dark => AppliedTheme.Dark,
            _ => app.ActualThemeVariant == ThemeVariant.Dark ? AppliedTheme.Dark : AppliedTheme.Light
        };

        if (actual != _appliedTheme)
        {
            _appliedTheme = actual;
            AppliedThemeChanged?.Invoke(this, actual);
        }
    }
}
