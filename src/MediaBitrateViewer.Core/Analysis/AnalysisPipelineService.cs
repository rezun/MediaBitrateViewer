using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.Models;
using Microsoft.Extensions.Logging;

namespace MediaBitrateViewer.Core.Analysis;

public sealed class AnalysisPipelineService : IAnalysisPipelineService
{
    private readonly IFileFingerprintService _fingerprintService;
    private readonly IFileProbeService _probeService;
    private readonly IFrameAnalysisService _frameAnalysisService;
    private readonly IAnalysisCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AnalysisPipelineService> _logger;

    public AnalysisPipelineService(
        IFileFingerprintService fingerprintService,
        IFileProbeService probeService,
        IFrameAnalysisService frameAnalysisService,
        IAnalysisCache cache,
        TimeProvider timeProvider,
        ILogger<AnalysisPipelineService> logger)
    {
        ArgumentNullException.ThrowIfNull(fingerprintService);
        ArgumentNullException.ThrowIfNull(probeService);
        ArgumentNullException.ThrowIfNull(frameAnalysisService);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _fingerprintService = fingerprintService;
        _probeService = probeService;
        _frameAnalysisService = frameAnalysisService;
        _cache = cache;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ProbedMediaFile> ProbeAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var fingerprint = await _fingerprintService.ComputeAsync(filePath, cancellationToken).ConfigureAwait(false);

        var probe = await _cache.TryGetProbeAsync(fingerprint, cancellationToken).ConfigureAwait(false);
        if (probe is null)
        {
            probe = await _probeService.ProbeAsync(filePath, fingerprint, cancellationToken).ConfigureAwait(false);
            await _cache.SaveProbeAsync(probe, cancellationToken).ConfigureAwait(false);
        }
        else if (!string.Equals(probe.FilePath, filePath, StringComparison.Ordinal))
        {
            // The cached probe was created when the file lived at a different path; refresh it
            // so downstream consumers always see the path the user actually opened.
            probe = probe with { FilePath = filePath };
            await _cache.SaveProbeAsync(probe, cancellationToken).ConfigureAwait(false);
        }
        return probe;
    }

    public ValueTask<CachedFrameAnalysis?> TryGetCachedAnalysisAsync(
        FileFingerprint fingerprint, int videoStreamIndex, CancellationToken cancellationToken)
        => _cache.TryGetCompleteFrameAnalysisAsync(fingerprint, videoStreamIndex, cancellationToken);

    public async Task<FrameAnalysisResult> RunFrameAnalysisAsync(
        ProbedMediaFile probe,
        VideoStreamInfo stream,
        IFrameAnalysisProgressObserver observer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(observer);

        await using var writer = _cache.BeginFrameAnalysis(probe.Fingerprint, stream.Index, _timeProvider);

        long frameCount = 0;
        try
        {
            await foreach (var frame in _frameAnalysisService
                .AnalyzeAsync(probe.FilePath, stream.Index, cancellationToken)
                .ConfigureAwait(false))
            {
                await writer.AppendAsync(frame, cancellationToken).ConfigureAwait(false);
                frameCount++;
                await observer.OnFrameAsync(frame, frameCount, cancellationToken).ConfigureAwait(false);
            }

            await writer.MarkCompleteAsync(CancellationToken.None).ConfigureAwait(false);
            return new FrameAnalysisResult(FrameAnalysisOutcome.Completed, frameCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await writer.MarkCanceledAsync().ConfigureAwait(false);
            return new FrameAnalysisResult(FrameAnalysisOutcome.Canceled, frameCount);
        }
        catch (Exception ex)
        {
            // Pipeline boundary: convert to result so callers can branch on outcome
            // and the cache entry is durably marked failed.
            _logger.LogError(ex, "Frame analysis failed for {Path} stream {Index}", probe.FilePath, stream.Index);
            await writer.MarkFailedAsync(ex.Message).ConfigureAwait(false);
            return new FrameAnalysisResult(FrameAnalysisOutcome.Failed, frameCount, ex.Message);
        }
    }

    public ValueTask ClearFileCacheAsync(FileFingerprint fingerprint)
        => _cache.ClearFileAsync(fingerprint);
}
