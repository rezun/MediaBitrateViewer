using MediaBitrateViewer.App.Services;

namespace MediaBitrateViewer.Tests.App;

public sealed class VideoFileExtensionsTests
{
    [Theory]
    [InlineData("sample.mp4")]
    [InlineData("sample.m4v")]
    [InlineData("sample.mkv")]
    [InlineData("sample.webm")]
    [InlineData("sample.mov")]
    [InlineData("sample.avi")]
    [InlineData("sample.ts")]
    [InlineData("sample.mts")]
    [InlineData("sample.m2ts")]
    [InlineData("sample.mpg")]
    [InlineData("sample.mpeg")]
    [InlineData("sample.mpe")]
    [InlineData("sample.flv")]
    [InlineData("sample.wmv")]
    [InlineData("sample.asf")]
    [InlineData("sample.3gp")]
    [InlineData("sample.3g2")]
    [InlineData("sample.ogv")]
    [InlineData("sample.mxf")]
    public void IsRecognized_ReturnsTrue_ForSupportedContainers(string path)
    {
        Assert.True(VideoFileExtensions.IsRecognized(path));
    }

    [Fact]
    public void LinuxMimeTypes_MapEverySupportedExtension()
    {
        Assert.Equal(
            VideoFileExtensions.Extensions.OrderBy(extension => extension),
            VideoFileExtensions.LinuxMimeTypesByExtension.Keys.OrderBy(extension => extension));
    }
}
