namespace MediaBitrateViewer.Core.Abstractions;

public interface IWindowCoordinator
{
    void OpenWindowFor(string filePath);
    void OpenInitialWindow();
    bool TryLoadInActiveEmptyWindow(string filePath);
}
