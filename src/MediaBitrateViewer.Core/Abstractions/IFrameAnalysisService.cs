using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Core.Abstractions;

public interface IFrameAnalysisService
{
    IAsyncEnumerable<FrameRecord> AnalyzeAsync(
        string filePath,
        int videoStreamIndex,
        CancellationToken cancellationToken);
}
