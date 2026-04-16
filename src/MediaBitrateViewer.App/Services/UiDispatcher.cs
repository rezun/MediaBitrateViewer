using Avalonia.Threading;
using MediaBitrateViewer.Core.Abstractions;

namespace MediaBitrateViewer.App.Services;

public sealed class UiDispatcher : IUiDispatcher
{
    public async ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Background, cancellationToken);
    }
}
