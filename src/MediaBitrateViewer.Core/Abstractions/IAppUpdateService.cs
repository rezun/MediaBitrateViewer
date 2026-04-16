namespace MediaBitrateViewer.Core.Abstractions;

public interface IAppUpdateService
{
    event EventHandler? StateChanged;

    bool IsUpdateReadyToInstall { get; }
    string UpdateButtonText { get; }

    Task StartAsync(CancellationToken cancellationToken);
    void ApplyPendingUpdateAndRestart();
    void ShowTestUpdateNotification();
}
