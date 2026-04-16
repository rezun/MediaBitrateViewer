namespace MediaBitrateViewer.Core.Models;

public readonly record struct VisibleTimeRange(double StartSeconds, double EndSeconds)
{
    public double Span => EndSeconds - StartSeconds;
    public bool IsValid => EndSeconds > StartSeconds;
}
