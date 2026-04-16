using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Core.Abstractions;

/// <summary>
/// Orchestrates fingerprint -> probe -> cache lookup -> frame analysis -> cache write.
/// Stateless: callers manage cancellation and consume per-frame progress through the observer.
/// </summary>
public interface IAnalysisPipelineService
{
    Task<ProbedMediaFile> ProbeAsync(string filePath, CancellationToken cancellationToken);

    ValueTask<CachedFrameAnalysis?> TryGetCachedAnalysisAsync(
        FileFingerprint fingerprint,
        int videoStreamIndex,
        CancellationToken cancellationToken);

    /// <summary>
    /// Streams frames from ffprobe, persists them through an <see cref="IFrameCacheWriter"/>,
    /// and notifies the observer per frame. The cache entry is marked complete, canceled,
    /// or failed depending on how the run terminated.
    /// </summary>
    Task<FrameAnalysisResult> RunFrameAnalysisAsync(
        ProbedMediaFile probe,
        VideoStreamInfo stream,
        IFrameAnalysisProgressObserver observer,
        CancellationToken cancellationToken);

    ValueTask ClearFileCacheAsync(FileFingerprint fingerprint);
}

public interface IFrameAnalysisProgressObserver
{
    ValueTask OnFrameAsync(FrameRecord frame, long totalFrames, CancellationToken cancellationToken);
}

public enum FrameAnalysisOutcome
{
    Completed,
    Canceled,
    Failed
}

public sealed record FrameAnalysisResult(
    FrameAnalysisOutcome Outcome,
    long FrameCount,
    string? ErrorMessage = null);
