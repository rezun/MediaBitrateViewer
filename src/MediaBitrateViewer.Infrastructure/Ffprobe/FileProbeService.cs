using System.Diagnostics;
using System.Globalization;
using MediaBitrateViewer.Core.Abstractions;
using MediaBitrateViewer.Core.Models;
using Microsoft.Extensions.Logging;

namespace MediaBitrateViewer.Infrastructure.Ffprobe;

public sealed class FileProbeService : IFileProbeService
{
    private readonly ILogger<FileProbeService> _logger;

    public FileProbeService(ILogger<FileProbeService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async ValueTask<ProbedMediaFile> ProbeAsync(
        string filePath,
        FileFingerprint fingerprint,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var psi = new ProcessStartInfo("ffprobe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-print_format");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add("-show_format");
        psi.ArgumentList.Add("-show_streams");
        psi.ArgumentList.Add(filePath);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffprobe process");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            _logger.LogError("ffprobe exited with {ExitCode}: {Stderr}", process.ExitCode, stderr);
            throw new FileProbeException($"ffprobe failed with exit code {process.ExitCode}: {stderr.Trim()}");
        }

        return ProbeJsonParser.Parse(filePath, fingerprint, stdout);
    }
}

