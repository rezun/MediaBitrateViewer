using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaBitrateViewer.Core.Formatting;
using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Core.ViewModels;

public sealed partial class LoadingPanelViewModel : ViewModelBase
{
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _operation = string.Empty;
    [ObservableProperty] private AnalysisProgress _progress;

    public string FrameCountText =>
        Progress.FramesProcessed.ToString("N0", CultureInfo.InvariantCulture);

    public string TimestampText =>
        TimeFormatter.Format(Progress.LatestTimestampSeconds);

    public double? FractionComplete => Progress.FractionComplete;

    public bool HasFractionalProgress => FractionComplete is not null;

    public string FractionPercentText => FractionComplete is { } f
        ? string.Create(CultureInfo.InvariantCulture, $"{f * 100:F0}%")
        : "—";

    partial void OnProgressChanged(AnalysisProgress value)
    {
        OnPropertyChanged(nameof(FrameCountText));
        OnPropertyChanged(nameof(TimestampText));
        OnPropertyChanged(nameof(FractionComplete));
        OnPropertyChanged(nameof(HasFractionalProgress));
        OnPropertyChanged(nameof(FractionPercentText));
    }
}
