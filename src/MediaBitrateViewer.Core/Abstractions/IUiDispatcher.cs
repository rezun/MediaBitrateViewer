namespace MediaBitrateViewer.Core.Abstractions;

public interface IUiDispatcher
{
    ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default);
}
