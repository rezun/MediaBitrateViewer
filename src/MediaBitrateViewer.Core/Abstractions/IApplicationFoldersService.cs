namespace MediaBitrateViewer.Core.Abstractions;

public interface IApplicationFoldersService
{
    Task OpenSettingsFolderAsync();
    Task OpenCacheFolderAsync();
}
