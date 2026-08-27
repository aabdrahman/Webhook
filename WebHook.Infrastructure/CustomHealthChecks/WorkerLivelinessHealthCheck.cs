using Microsoft.Extensions.Diagnostics.HealthChecks;
using WebHook.Infrastructure.Utilities;

namespace WebHook.Infrastructure.CustomHealthChecks;

public sealed class WorkerLivelinessHealthCheck : IHealthCheck
{
    private readonly WorkerLivenessTracker _workerLivenessTracker;

    public WorkerLivelinessHealthCheck(WorkerLivenessTracker workerLivenessTracker)
    {
        _workerLivenessTracker = workerLivenessTracker;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var activeStatus = _workerLivenessTracker.CheckActiveStatus();

        return activeStatus.Item1 ?
            Task.FromResult(new HealthCheckResult(status: HealthStatus.Healthy, description: $"Workers are running. Last updated at: {activeStatus.Item2}")) :
            Task.FromResult(new HealthCheckResult(status: HealthStatus.Degraded, description: $"Workers have stopped. Last updated at: {activeStatus.Item2}"));
    }
}