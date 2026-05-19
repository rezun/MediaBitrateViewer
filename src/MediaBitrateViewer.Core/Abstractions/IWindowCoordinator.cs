namespace MediaBitrateViewer.Core.Abstractions;

public interface IWindowCoordinator
{
    void OpenWindowFor(string filePath);
    void OpenInitialWindow(string? initialFilePath = null);
    bool TryLoadInActiveEmptyWindow(string filePath);
}
