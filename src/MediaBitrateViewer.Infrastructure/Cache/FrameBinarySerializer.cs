using System.Buffers.Binary;
using MediaBitrateViewer.Core.Models;

namespace MediaBitrateViewer.Infrastructure.Cache;

internal static class FrameBinarySerializer
{
    // Wire format per frame (20 bytes):
    //   double TimestampSeconds (8)
    //   double DurationSeconds  (8)
    //   int    PacketSizeBytes  (4)
    public const int RecordSize = 20;

    public static void Write(Span<byte> destination, in FrameRecord frame)
    {
        if (destination.Length < RecordSize)
            throw new ArgumentException("Buffer too small", nameof(destination));

        BinaryPrimitives.WriteDoubleLittleEndian(destination[..8], frame.TimestampSeconds);
        BinaryPrimitives.WriteDoubleLittleEndian(destination.Slice(8, 8), frame.DurationSeconds);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(16, 4), frame.PacketSizeBytes);
    }

    public static FrameRecord Read(ReadOnlySpan<byte> source)
    {
        if (source.Length < RecordSize)
            throw new InvalidDataException("Frame record truncated");

        return new FrameRecord
        {
            TimestampSeconds = BinaryPrimitives.ReadDoubleLittleEndian(source[..8]),
            DurationSeconds = BinaryPrimitives.ReadDoubleLittleEndian(source.Slice(8, 8)),
            PacketSizeBytes = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(16, 4))
        };
    }

    public static async ValueTask<IReadOnlyList<FrameRecord>> ReadAllAsync(string path, long expectedCount, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, useAsync: true);

        var length = stream.Length;
        if (length % RecordSize != 0)
            throw new InvalidDataException("Frame stream length not aligned to record size");
        if (expectedCount <= 0 || expectedCount > Array.MaxLength || length / RecordSize != expectedCount)
            throw new InvalidDataException("Frame stream length does not match the manifest count");

        var count = (int)(length / RecordSize);
        var frames = new FrameRecord[count];

        var buffer = new byte[(int)Math.Min(64 * 1024, length)];
        var offset = 0;
        var index = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            var available = offset + read;

            var consumed = 0;
            while (available - consumed >= RecordSize)
            {
                if (index == count) throw new InvalidDataException("Frame stream grew while reading");
                frames[index++] = Read(buffer.AsSpan(consumed, RecordSize));
                consumed += RecordSize;
            }

            var remainder = available - consumed;
            if (remainder > 0)
                Buffer.BlockCopy(buffer, consumed, buffer, 0, remainder);
            offset = remainder;
        }

        if (index != count)
            throw new InvalidDataException("Frame count mismatch");

        return frames;
    }
}
