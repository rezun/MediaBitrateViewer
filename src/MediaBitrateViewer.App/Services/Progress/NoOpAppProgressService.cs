using MediaBitrateViewer.Core.Abstractions;

namespace MediaBitrateViewer.App.Services.Progress;

public sealed class NoOpAppProgressService : IAppProgressService
{
    public void SetProgress(double fraction) { }
    public void Clear() { }
}
