using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Core.Abstractions;

public interface IFileProbeService
{
    ValueTask<ProbedMediaFile> ProbeAsync(string filePath, FileFingerprint fingerprint, CancellationToken cancellationToken);
}
