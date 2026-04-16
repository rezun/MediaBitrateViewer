using System.Buffers;
using System.Security.Cryptography;
using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Infrastructure.Files;

public sealed class FileFingerprintService : IFileFingerprintService
{
    private const int HashChunkBytes = 4 * 1024 * 1024;

    public async ValueTask<FileFingerprint> ComputeAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var info = new FileInfo(filePath);
        if (!info.Exists)
            throw new FileNotFoundException("File does not exist", filePath);

        var length = info.Length;
        var lastWrite = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1,
            useAsync: true);

        var headHash = await HashChunkAsync(stream, 0, HashChunkBytes, length, cancellationToken).ConfigureAwait(false);

        var tailOffset = Math.Max(0, length - HashChunkBytes);
        string tailHash;
        if (tailOffset == 0)
        {
            tailHash = headHash;
        }
        else
        {
            tailHash = await HashChunkAsync(stream, tailOffset, HashChunkBytes, length, cancellationToken).ConfigureAwait(false);
        }

        return new FileFingerprint(length, lastWrite, headHash, tailHash);
    }

    private static async ValueTask<string> HashChunkAsync(
        FileStream stream,
        long offset,
        int requestedSize,
        long totalLength,
        CancellationToken cancellationToken)
    {
        var sizeToRead = (int)Math.Min(requestedSize, totalLength - offset);
        if (sizeToRead <= 0) return string.Empty;

        stream.Seek(offset, SeekOrigin.Begin);

        var buffer = ArrayPool<byte>.Shared.Rent(sizeToRead);
        try
        {
            var memory = buffer.AsMemory(0, sizeToRead);
            var read = 0;
            while (read < sizeToRead)
            {
                var n = await stream.ReadAsync(memory.Slice(read), cancellationToken).ConfigureAwait(false);
                if (n == 0) break;
                read += n;
            }

            var hashBytes = SHA256.HashData(buffer.AsSpan(0, read));
            return Convert.ToHexString(hashBytes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
