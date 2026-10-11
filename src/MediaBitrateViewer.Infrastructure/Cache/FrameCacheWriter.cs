using System.Text.Json;
using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.Models;
using Microsoft.Extensions.Logging;

namespace MediaBitrateViewer.Infrastructure.Cache;

internal sealed class FrameCacheWriter : IFrameCacheWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly string _manifestPath;
    private readonly string _framesPath;
    private readonly string _temporaryFramesPath;
    private readonly string _temporaryManifestPath;
    private readonly FileFingerprint _fingerprint;
    private readonly int _streamIndex;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly DateTimeOffset _createdAt;
    private readonly FileStream _frameStream;
    private readonly byte[] _scratch = new byte[FrameBinarySerializer.RecordSize];

    private long _frameCount;
    private bool _published;
    private bool _finished;
    private bool _disposed;

    public FrameCacheWriter(string manifestPath, FileFingerprint fingerprint, int streamIndex,
        TimeProvider timeProvider, ILogger logger)
    {
        _manifestPath = manifestPath;
        _fingerprint = fingerprint;
        _streamIndex = streamIndex;
        _timeProvider = timeProvider;
        _logger = logger;
        _createdAt = _timeProvider.GetUtcNow();

        var directory = Path.GetDirectoryName(manifestPath)
            ?? throw new ArgumentException("Manifest path needs a parent directory", nameof(manifestPath));
        var id = Guid.NewGuid().ToString("N");
        _framesPath = Path.Combine(directory, $"stream-{streamIndex}.{id}.frames.bin");
        _temporaryFramesPath = _framesPath + ".tmp";
        _temporaryManifestPath = manifestPath + $".{id}.tmp";
        _frameStream = new FileStream(
            _temporaryFramesPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 64 * 1024, useAsync: true);
    }

    public async ValueTask AppendAsync(FrameRecord frame, CancellationToken cancellationToken)
    {
        ThrowIfFinished();
        FrameBinarySerializer.Write(_scratch, frame);
        await _frameStream.WriteAsync(_scratch.AsMemory(0, FrameBinarySerializer.RecordSize), cancellationToken).ConfigureAwait(false);
        _frameCount++;
    }

    /// <summary>
    /// Publishes only complete, immutable frame files. Cancellation before publication
    /// leaves the previous manifest intact; disposal removes this writer's private files.
    /// After the last cancellation check, the two renames finish without cancellation.
    /// A failure between renames leaves an unreferenced frame file, removed on disposal.
    /// </summary>
    public async ValueTask MarkCompleteAsync(CancellationToken cancellationToken)
    {
        ThrowIfFinished();
        if (_frameCount == 0)
            throw new InvalidOperationException("An empty frame analysis cannot be cached as complete.");

        await _frameStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        await _frameStream.DisposeAsync().ConfigureAwait(false);

        var manifest = new FrameManifest
        {
            FramesFileName = Path.GetFileName(_framesPath),
            CacheVersion = AnalysisCache.CacheVersion,
            Fingerprint = _fingerprint,
            StreamIndex = _streamIndex,
            Status = CacheEntryStatus.Complete,
            CreatedAt = _createdAt,
            UpdatedAt = _timeProvider.GetUtcNow(),
            FrameCount = _frameCount
        };
        await using (var stream = File.Create(_temporaryManifestPath))
        {
            await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        File.Move(_temporaryFramesPath, _framesPath);
        // Readers hold one manifest snapshot pointing to an immutable generation.
        // Concurrent writers can publish in either order without mixing their data.
        File.Move(_temporaryManifestPath, _manifestPath, overwrite: true);
        _published = true;
        _finished = true;
    }

    public ValueTask MarkCanceledAsync()
    {
        _finished = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask MarkFailedAsync(string errorSummary)
    {
        ArgumentNullException.ThrowIfNull(errorSummary);
        _finished = true;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await _frameStream.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            DeletePrivateFile(_temporaryFramesPath);
            DeletePrivateFile(_temporaryManifestPath);
            if (!_published) DeletePrivateFile(_framesPath);
        }
    }

    private void ThrowIfFinished()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished) throw new InvalidOperationException("Frame analysis has already finished.");
    }

    private void DeletePrivateFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not remove private analysis cache file {Path}", path);
        }
    }
}
