namespace MediaBitrateViewer.Core.Abstractions;

public interface IFilePickerService
{
    ValueTask<string?> PickVideoFileAsync(CancellationToken cancellationToken);
}
