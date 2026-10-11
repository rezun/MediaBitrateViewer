using System.Text.Json;
using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.Models;
using Microsoft.Extensions.Logging;

namespace MediaBitrateViewer.Infrastructure.Cache;

public sealed class AnalysisCache : IAnalysisCache
{
    // v3 invalidates empty packet analyses produced before the DTS fallback and
    // publishes immutable frame files through an atomic manifest replacement.
    public const string CacheVersion = "v3";
    private const string ProbeFileName = "probe.json";

    private readonly string _rootDirectory;
    private readonly ILogger<AnalysisCache> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public AnalysisCache(string rootDirectory, ILogger<AnalysisCache> logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);
        ArgumentNullException.ThrowIfNull(logger);
        _rootDirectory = rootDirectory;
        _logger = logger;
        Directory.CreateDirectory(_rootDirectory);
    }

    public string RootDirectory => _rootDirectory;

    private string FileDirectory(FileFingerprint fp) =>
        Path.Combine(_rootDirectory, CacheVersion, fp.ToCacheKey());

    private string ProbePath(FileFingerprint fp) =>
        Path.Combine(FileDirectory(fp), ProbeFileName);

    private string ManifestPath(FileFingerprint fp, int streamIndex) =>
        Path.Combine(FileDirectory(fp), $"stream-{streamIndex}.manifest.json");

    public async ValueTask<ProbedMediaFile?> TryGetProbeAsync(FileFingerprint fingerprint, CancellationToken cancellationToken)
    {
        var path = ProbePath(fingerprint);
        if (!File.Exists(path)) return null;

        try
        {
            await using var stream = OpenManifest(path);
            var dto = await JsonSerializer.DeserializeAsync<ProbeCacheEntry>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return dto is not null && dto.Fingerprint.Equals(fingerprint)
                && string.Equals(dto.CacheVersion, CacheVersion, StringComparison.Ordinal) ? dto.ToDomain() : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Discarding unreadable probe cache at {Path}", path);
            return null;
        }
    }

    public async ValueTask SaveProbeAsync(ProbedMediaFile probe, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var dir = FileDirectory(probe.Fingerprint);
        Directory.CreateDirectory(dir);
        var path = ProbePath(probe.Fingerprint);

        var dto = ProbeCacheEntry.From(probe);
        var tmp = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, dto, JsonOptions, cancellationToken).ConfigureAwait(false);
            }
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            DeleteTemporaryFile(tmp);
        }
    }

    public async ValueTask<CachedFrameAnalysis?> TryGetCompleteFrameAnalysisAsync(
        FileFingerprint fingerprint,
        int videoStreamIndex,
        CancellationToken cancellationToken)
    {
        var manifestPath = ManifestPath(fingerprint, videoStreamIndex);
        if (!File.Exists(manifestPath))
            return null;

        FrameManifest? manifest;
        try
        {
            await using var ms = OpenManifest(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<FrameManifest>(ms, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Discarding unreadable manifest at {Path}", manifestPath);
            return null;
        }

        if (manifest is null || manifest.Status != CacheEntryStatus.Complete)
            return null;
        if (!string.Equals(manifest.CacheVersion, CacheVersion, StringComparison.Ordinal))
            return null;
        if (!manifest.Fingerprint.Equals(fingerprint))
            return null;
        if (manifest.StreamIndex != videoStreamIndex || manifest.FrameCount <= 0
            || string.IsNullOrEmpty(manifest.FramesFileName)
            || !string.Equals(Path.GetFileName(manifest.FramesFileName), manifest.FramesFileName, StringComparison.Ordinal)
            || manifest.FramesFileName.IndexOfAny(['/', '\\']) >= 0)
            return null;

        try
        {
            var framesPath = Path.Combine(FileDirectory(fingerprint), manifest.FramesFileName);
            var frames = await FrameBinarySerializer.ReadAllAsync(framesPath, manifest.FrameCount, cancellationToken).ConfigureAwait(false);
            return new CachedFrameAnalysis(fingerprint, videoStreamIndex, frames, manifest.UpdatedAt);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            _logger.LogWarning(ex, "Discarding unreadable frames cache for {Path}", manifestPath);
            return null;
        }
    }

    public IFrameCacheWriter BeginFrameAnalysis(FileFingerprint fingerprint, int videoStreamIndex, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var dir = FileDirectory(fingerprint);
        Directory.CreateDirectory(dir);
        return new FrameCacheWriter(
            ManifestPath(fingerprint, videoStreamIndex),
            fingerprint,
            videoStreamIndex,
            timeProvider,
            _logger);
    }

    private static FileStream OpenManifest(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, useAsync: true);

    private void DeleteTemporaryFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not remove temporary cache file {Path}", path);
        }
    }

    public ValueTask ClearFileAsync(FileFingerprint fingerprint)
    {
        var dir = FileDirectory(fingerprint);
        if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException ex) { _logger.LogWarning(ex, "Failed to delete cache dir {Dir}", dir); }
        }
        return ValueTask.CompletedTask;
    }
}
