using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Core.Abstractions;

public interface IAnalysisCache
{
    ValueTask<ProbedMediaFile?> TryGetProbeAsync(FileFingerprint fingerprint, CancellationToken cancellationToken);
    ValueTask SaveProbeAsync(ProbedMediaFile probe, CancellationToken cancellationToken);

    ValueTask<CachedFrameAnalysis?> TryGetCompleteFrameAnalysisAsync(
        FileFingerprint fingerprint,
        int videoStreamIndex,
        CancellationToken cancellationToken);

    IFrameCacheWriter BeginFrameAnalysis(FileFingerprint fingerprint, int videoStreamIndex, TimeProvider timeProvider);

    ValueTask ClearFileAsync(FileFingerprint fingerprint);
}

public sealed record CachedFrameAnalysis(
    FileFingerprint Fingerprint,
    int VideoStreamIndex,
    IReadOnlyList<FrameRecord> Frames,
    DateTimeOffset CompletedAt);

public interface IFrameCacheWriter : IAsyncDisposable
{
    ValueTask AppendAsync(FrameRecord frame, CancellationToken cancellationToken);
    ValueTask MarkCompleteAsync(CancellationToken cancellationToken);
    ValueTask MarkCanceledAsync();
    ValueTask MarkFailedAsync(string errorSummary);
}
