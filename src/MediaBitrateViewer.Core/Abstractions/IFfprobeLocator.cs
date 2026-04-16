namespace MediaBitrateViewer.Core.Abstractions;

public interface IFfprobeLocator
{
    ValueTask<FfprobeLocation> LocateAsync(CancellationToken cancellationToken);
}

public sealed record FfprobeLocation(string? ExecutablePath, bool IsAvailable, string? VersionString);
