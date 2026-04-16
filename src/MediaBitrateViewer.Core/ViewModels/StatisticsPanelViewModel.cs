using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaBitrateViewer.Core.Formatting;
using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Core.ViewModels;

public sealed partial class StatisticsPanelViewModel : ViewModelBase
{
    [ObservableProperty] private VisibleRangeStatistics _statistics = VisibleRangeStatistics.Empty;

    public bool HasData => Statistics.SampleCount > 0;
    public string MinText => Format(Statistics.MinMbps);
    public string MaxText => Format(Statistics.MaxMbps);
    public string AverageText => Format(Statistics.AverageMbps);
    public string Percentile95Text => Format(Statistics.Percentile95Mbps);
    public string SampleCountText => Statistics.SampleCount.ToString("N0", CultureInfo.InvariantCulture);
    public string RangeText => Statistics.Range.IsValid
        ? string.Create(CultureInfo.InvariantCulture,
            $"{TimeFormatter.Format(Statistics.Range.StartSeconds)} \u2013 {TimeFormatter.Format(Statistics.Range.EndSeconds)}")
        : "\u2014";

    partial void OnStatisticsChanged(VisibleRangeStatistics value)
    {
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(MinText));
        OnPropertyChanged(nameof(MaxText));
        OnPropertyChanged(nameof(AverageText));
        OnPropertyChanged(nameof(Percentile95Text));
        OnPropertyChanged(nameof(SampleCountText));
        OnPropertyChanged(nameof(RangeText));
    }

    private static string Format(double mbps) =>
        string.Create(CultureInfo.InvariantCulture, $"{mbps:F2} Mbps");
}
