namespace MediaBitrateViewer.App.Services;

internal static class VideoFileExtensions
{
    public static readonly IReadOnlyList<string> Extensions =
    [
        ".mp4",
        ".m4v",
        ".mkv",
        ".webm",
        ".mov",
        ".avi",
        ".ts",
        ".mts",
        ".m2ts",
        ".mpg",
        ".mpeg",
        ".mpe",
        ".flv",
        ".wmv",
        ".asf",
        ".3gp",
        ".3g2",
        ".ogv",
        ".mxf"
    ];

    public static readonly IReadOnlyList<string> Patterns = Extensions
        .Select(extension => $"*{extension}")
        .ToArray();

    public static readonly IReadOnlyDictionary<string, string> LinuxMimeTypesByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4",
        [".m4v"] = "video/mp4",
        [".mkv"] = "video/x-matroska",
        [".webm"] = "video/webm",
        [".mov"] = "video/quicktime",
        [".avi"] = "video/x-msvideo",
        [".ts"] = "video/mp2t",
        [".mts"] = "video/mp2t",
        [".m2ts"] = "video/mp2t",
        [".mpg"] = "video/mpeg",
        [".mpeg"] = "video/mpeg",
        [".mpe"] = "video/mpeg",
        [".flv"] = "video/x-flv",
        [".wmv"] = "video/x-ms-wmv",
        [".asf"] = "video/x-ms-asf",
        [".3gp"] = "video/3gpp",
        [".3g2"] = "video/3gpp2",
        [".ogv"] = "video/ogg",
        [".mxf"] = "application/mxf"
    };

    private static readonly HashSet<string> RecognizedExtensions = new(
        Extensions,
        StringComparer.OrdinalIgnoreCase);

    public static bool IsRecognized(string path) =>
        RecognizedExtensions.Contains(Path.GetExtension(path));
}
