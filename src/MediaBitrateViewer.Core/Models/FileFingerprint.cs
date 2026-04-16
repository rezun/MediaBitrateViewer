namespace MediaBitrateViewer.Core.Models;

public readonly record struct FileFingerprint(
    long FileLength,
    DateTimeOffset LastWriteTimeUtc,
    string HeadHashHex,
    string TailHashHex)
{
    public string ToCacheKey() =>
        $"{FileLength:x16}-{LastWriteTimeUtc.UtcTicks:x16}-{HeadHashHex}-{TailHashHex}";
}
