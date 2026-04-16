using Avalonia;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.MaterialDesign;
using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace MediaBitrateViewer.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        EnsureBundledLaunchPath();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // macOS apps launched from Finder/Dock get a minimal LaunchServices PATH that
    // excludes Homebrew/MacPorts. Linux .desktop launches are similarly stripped.
    // Prepend the common install locations so ffprobe resolves the same way as
    // when launched from a shell.
    private static void EnsureBundledLaunchPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        string[] extras = ["/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin"];
        var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var segments = current.Split(':', StringSplitOptions.RemoveEmptyEntries);
        var missing = extras.Where(e => !segments.Contains(e, StringComparer.Ordinal)).ToArray();

        if (missing.Length == 0)
            return;

        var combined = string.Join(':', missing) + (current.Length > 0 ? ":" + current : string.Empty);
        Environment.SetEnvironmentVariable("PATH", combined);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        IconProvider.Current.Register<MaterialDesignIconProvider>();

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
