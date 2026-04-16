using System;
using System.Globalization;
using Avalonia.Data.Converters;
using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.App.Converters;

public sealed class EnumDisplayConverter : IValueConverter
{
    public static readonly EnumDisplayConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        GraphMode mode => mode.ToDisplayLabel(),
        RollingAverageWindow window => window.ToDisplayLabel(),
        ThemeMode theme => theme switch
        {
            ThemeMode.System => "System",
            ThemeMode.Dark => "Dark",
            ThemeMode.Light => "Light",
            _ => theme.ToString()
        },
        null => null,
        _ => value.ToString()
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
