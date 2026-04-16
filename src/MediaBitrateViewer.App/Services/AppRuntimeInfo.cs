using MediaBitrateViewer.Core.Abstractions;

namespace MediaBitrateViewer.App.Services;

public sealed class AppRuntimeInfo : IAppRuntimeInfo
{
    public bool IsDevelopmentEnvironment { get; } =
        string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
            "Development",
            StringComparison.OrdinalIgnoreCase);
}
