using System.Diagnostics;
using MediaBitrateViewer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MediaBitrateViewer.Infrastructure.Ffprobe;

public sealed class FfprobeLocator : IFfprobeLocator
{
    private readonly ILogger<FfprobeLocator> _logger;

    public FfprobeLocator(ILogger<FfprobeLocator> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async ValueTask<FfprobeLocation> LocateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo("ffprobe", "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return new FfprobeLocation(null, false, null);

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            _ = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("ffprobe -version exited with code {ExitCode}", process.ExitCode);
                return new FfprobeLocation(null, false, null);
            }

            var firstLine = stdout.Split('\n', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return new FfprobeLocation("ffprobe", true, firstLine?.Trim());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Could not start ffprobe; assuming it is not on PATH");
            return new FfprobeLocation(null, false, null);
        }
    }
}
