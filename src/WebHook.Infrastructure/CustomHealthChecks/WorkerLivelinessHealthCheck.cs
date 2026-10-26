using Microsoft.Extensions.Diagnostics.HealthChecks;
using WebHook.Infrastructure.Utilities;

namespace WebHook.Infrastructure.CustomHealthChecks;

/// <summary>
/// Reports whether the background workers have sent a heartbeat recently, using
/// <see cref="WorkerLivenessTracker"/>. <see cref="HealthStatus.Degraded"/> when they have not.
/// </summary>
public sealed class WorkerLivelinessHealthCheck : IHealthCheck
{
    private readonly WorkerLivenessTracker _workerLivenessTracker;

    /// <summary>
    /// Initialises a new instance of the <see cref="WorkerLivelinessHealthCheck"/> class.
    /// </summary>
    /// <param name="workerLivenessTracker">The shared heartbeat tracker.</param>
    public WorkerLivelinessHealthCheck(WorkerLivenessTracker workerLivenessTracker)
    {
        _workerLivenessTracker = workerLivenessTracker;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var activeStatus = _workerLivenessTracker.CheckActiveStatus();

        return activeStatus.Item1 ?
            Task.FromResult(new HealthCheckResult(status: HealthStatus.Healthy, description: $"Workers are running. Last updated at: {activeStatus.Item2}")) :
            Task.FromResult(new HealthCheckResult(status: HealthStatus.Degraded, description: $"Workers have stopped. Last updated at: {activeStatus.Item2}"));
    }
}