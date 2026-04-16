using System.Text.Json;
using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Infrastructure.Cache;

internal sealed class FrameCacheWriter : IFrameCacheWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly string _manifestPath;
    private readonly string _framesPath;
    private readonly FileFingerprint _fingerprint;
    private readonly int _streamIndex;
    private readonly TimeProvider _timeProvider;
    private readonly DateTimeOffset _createdAt;
    private readonly FileStream _frameStream;
    private readonly byte[] _scratch = new byte[FrameBinarySerializer.RecordSize];

    private long _frameCount;
    private CacheEntryStatus _status = CacheEntryStatus.InProgress;
    private string? _errorSummary;
    private bool _disposed;

    public FrameCacheWriter(string manifestPath, string framesPath, FileFingerprint fingerprint, int streamIndex, TimeProvider timeProvider)
    {
        _manifestPath = manifestPath;
        _framesPath = framesPath;
        _fingerprint = fingerprint;
        _streamIndex = streamIndex;
        _timeProvider = timeProvider;
        _createdAt = _timeProvider.GetUtcNow();

        _frameStream = new FileStream(
            framesPath, FileMode.Create, FileAccess.Write, FileShare.Read,
            bufferSize: 64 * 1024, useAsync: true);

        WriteManifestSync();
    }

    public async ValueTask AppendAsync(FrameRecord frame, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        FrameBinarySerializer.Write(_scratch, frame);
        await _frameStream.WriteAsync(_scratch.AsMemory(0, FrameBinarySerializer.RecordSize), cancellationToken).ConfigureAwait(false);
        _frameCount++;
    }

    public async ValueTask MarkCompleteAsync(CancellationToken cancellationToken)
    {
        _status = CacheEntryStatus.Complete;
        await _frameStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        await WriteManifestAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask MarkCanceledAsync()
    {
        _status = CacheEntryStatus.Canceled;
        try { await _frameStream.FlushAsync(CancellationToken.None).ConfigureAwait(false); } catch { /* best effort */ }
        await WriteManifestAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async ValueTask MarkFailedAsync(string errorSummary)
    {
        _status = CacheEntryStatus.Failed;
        _errorSummary = errorSummary;
        try { await _frameStream.FlushAsync(CancellationToken.None).ConfigureAwait(false); } catch { /* best effort */ }
        await WriteManifestAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _frameStream.DisposeAsync().ConfigureAwait(false);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void WriteManifestSync()
    {
        var manifest = BuildManifest();
        File.WriteAllText(_manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private async ValueTask WriteManifestAsync(CancellationToken cancellationToken)
    {
        var manifest = BuildManifest();
        var tmp = _manifestPath + ".tmp";
        await using (var s = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(s, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        File.Move(tmp, _manifestPath, overwrite: true);
    }

    private FrameManifest BuildManifest() => new()
    {
        CacheVersion = AnalysisCache.CacheVersion,
        Fingerprint = _fingerprint,
        StreamIndex = _streamIndex,
        Status = _status,
        CreatedAt = _createdAt,
        UpdatedAt = _timeProvider.GetUtcNow(),
        FrameCount = _frameCount,
        ErrorSummary = _errorSummary
    };
}
