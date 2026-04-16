using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaBitrateViewer.Core.Formatting;
using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Core.ViewModels;

public sealed partial class CursorReadoutViewModel : ViewModelBase
{
    [ObservableProperty] private CursorReadout _readout = CursorReadout.Empty(GraphMode.PerSecond);

    public string TimeText => Readout.HasValue ? TimeFormatter.Format(Readout.TimeSeconds) : "\u2014";
    public string BitrateText => Readout.HasValue
        ? string.Create(CultureInfo.InvariantCulture, $"{Readout.BitrateMbps:F2} Mbps")
        : "\u2014";
    public string ModeText => Readout.Mode.ToDisplayLabel();

    partial void OnReadoutChanged(CursorReadout value)
    {
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(BitrateText));
        OnPropertyChanged(nameof(ModeText));
    }
}
