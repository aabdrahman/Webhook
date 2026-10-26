using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WebHook.Infrastructure.CustomHealthChecks;

/// <summary>
/// Reports free space on the host's C: drive.
/// </summary>
/// <remarks>
/// <see cref="HealthStatus.Degraded"/> when free space is below the minimum or within 45% above it;
/// <see cref="HealthStatus.Unhealthy"/> when no ready C: drive is found (for example, on Linux).
/// </remarks>
public sealed class C_DriveHealthCheck : IHealthCheck
{
    private readonly long _minimumAvailableSizeInBytes;

    /// <summary>
    /// Initialises a new instance of the <see cref="C_DriveHealthCheck"/> class.
    /// </summary>
    /// <param name="minimumAvailableSizeInMb">The minimum free space, in megabytes, before the check degrades.</param>
    public C_DriveHealthCheck(int minimumAvailableSizeInMb = 250)
    {
        _minimumAvailableSizeInBytes = minimumAvailableSizeInMb * 1024 * 1024;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            DriveInfo[] drives = DriveInfo.GetDrives();
            DriveInfo? cDriveInfo = drives.FirstOrDefault(x => x.Name.StartsWith("C") && x.IsReady);

            if (cDriveInfo is null)
            {
                return Task.FromResult(new HealthCheckResult(status: HealthStatus.Unhealthy, description: "The system C drive information could not be fetched."));
            }

            long availableSpace = cDriveInfo.TotalFreeSpace;

            if (availableSpace < _minimumAvailableSizeInBytes)
            {
                return Task.FromResult(new HealthCheckResult(status: HealthStatus.Degraded, description: $"Low disk space in the C Drive. Available space in Mb: {availableSpace / (1024 * 1024)}"));
            }

            if (availableSpace < _minimumAvailableSizeInBytes * 1.45)
            {
                return Task.FromResult(new HealthCheckResult(status: HealthStatus.Degraded, description: $"Disk space is growing on the C drive. Available space in Mb: {availableSpace / (1024 * 1024)}"));
            }

            return Task.FromResult(new HealthCheckResult(status: HealthStatus.Healthy, description: "C Drive disk space is running optimally on enough space."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HealthCheckResult(status: HealthStatus.Degraded, description: "An error occurred while getting C Drvice information", exception: ex));
        }

    }
}
