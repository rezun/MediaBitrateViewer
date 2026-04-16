namespace MediaBitrateViewer.Core.Abstractions;

/// <summary>
/// Marks a class that requires asynchronous initialization after construction.
/// </summary>
public interface IAsyncInitializable
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
