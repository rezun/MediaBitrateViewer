using System.Collections.ObjectModel;

namespace MediaBitrateViewer.Core.Abstractions;

public interface IRecentFilesService
{
    ReadOnlyObservableCollection<string> Files { get; }

    Task AddAsync(string filePath);

    Task ClearAsync();
}
