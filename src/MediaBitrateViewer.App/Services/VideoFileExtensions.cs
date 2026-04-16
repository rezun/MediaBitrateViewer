namespace MediaBitrateViewer.App.Services;

internal static class VideoFileExtensions
{
    public static readonly IReadOnlyList<string> Patterns = new[]
    {
        "*.mp4", "*.mkv", "*.mov", "*.avi", "*.webm", "*.m4v",
        "*.ts", "*.mpg", "*.mpeg", "*.flv", "*.wmv", "*.3gp"
    };

    private static readonly HashSet<string> Extensions = new(
        Patterns.Select(p => p.Replace("*", "", StringComparison.Ordinal)),
        StringComparer.OrdinalIgnoreCase);

    public static bool IsRecognized(string path) =>
        Extensions.Contains(Path.GetExtension(path));
}
