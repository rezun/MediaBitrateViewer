using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Core.Abstractions;

public interface IFileFingerprintService
{
    ValueTask<FileFingerprint> ComputeAsync(string filePath, CancellationToken cancellationToken);
}
