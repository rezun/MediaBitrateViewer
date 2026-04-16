using System.Security.Cryptography;
using MediaBitrateViewer.Infrastructure.Files;

namespace MediaBitrateViewer.Tests.Files;

public sealed class FileFingerprintServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mbv-tests-" + Guid.NewGuid().ToString("N"));

    public FileFingerprintServiceTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task SmallFile_HeadAndTailHashEqual()
    {
        var path = Path.Combine(_dir, "small.bin");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });

        var svc = new FileFingerprintService();
        var fp = await svc.ComputeAsync(path, CancellationToken.None);

        Assert.Equal(5, fp.FileLength);
        Assert.Equal(fp.HeadHashHex, fp.TailHashHex);
        Assert.NotEmpty(fp.HeadHashHex);
    }

    [Fact]
    public async Task LargeFile_HeadAndTailHashesDifferentForVaryingContent()
    {
        var path = Path.Combine(_dir, "large.bin");
        var bytes = new byte[8 * 1024 * 1024 + 100];
        new Random(42).NextBytes(bytes);
        File.WriteAllBytes(path, bytes);

        var svc = new FileFingerprintService();
        var fp = await svc.ComputeAsync(path, CancellationToken.None);

        Assert.Equal(bytes.Length, fp.FileLength);
        Assert.NotEqual(fp.HeadHashHex, fp.TailHashHex);
    }

    [Fact]
    public async Task IdenticalFiles_ProduceIdenticalFingerprints()
    {
        var p1 = Path.Combine(_dir, "a.bin");
        var p2 = Path.Combine(_dir, "b.bin");
        var bytes = new byte[1024];
        new Random(1).NextBytes(bytes);
        File.WriteAllBytes(p1, bytes);
        File.WriteAllBytes(p2, bytes);

        // Force identical timestamps
        var ts = DateTime.UtcNow.AddDays(-1);
        File.SetLastWriteTimeUtc(p1, ts);
        File.SetLastWriteTimeUtc(p2, ts);

        var svc = new FileFingerprintService();
        var fp1 = await svc.ComputeAsync(p1, CancellationToken.None);
        var fp2 = await svc.ComputeAsync(p2, CancellationToken.None);

        Assert.Equal(fp1.HeadHashHex, fp2.HeadHashHex);
        Assert.Equal(fp1.TailHashHex, fp2.TailHashHex);
        Assert.Equal(fp1.FileLength, fp2.FileLength);
        Assert.Equal(fp1.ToCacheKey(), fp2.ToCacheKey());
    }
}
