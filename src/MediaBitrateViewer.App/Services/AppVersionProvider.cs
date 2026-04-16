using System.Reflection;
using MediaBitrateViewer.Core.Abstractions;

namespace MediaBitrateViewer.App.Services;

public sealed class AppVersionProvider : IAppVersionProvider
{
    public string DisplayVersion { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var assembly = typeof(AppVersionProvider).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plusIndex = informational.IndexOf('+', StringComparison.Ordinal);
            return plusIndex >= 0 ? informational[..plusIndex] : informational;
        }

        var assemblyVersion = assembly.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(assemblyVersion) ? "unknown" : assemblyVersion;
    }
}
