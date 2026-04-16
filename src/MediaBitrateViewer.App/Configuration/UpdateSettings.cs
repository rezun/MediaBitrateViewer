namespace MediaBitrateViewer.App.Configuration;

public sealed class UpdateSettings
{
    public const string SectionName = "Updates";

    public bool Enabled { get; set; } = true;
    public string GithubRepositoryUrl { get; set; } = "https://github.com/rezun/MediaBitrateViewer";
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(4);
}
