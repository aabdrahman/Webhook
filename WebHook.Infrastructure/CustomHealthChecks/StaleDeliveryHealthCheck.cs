using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.CustomHealthChecks;

public sealed class StaleDeliveryHealthCheck : IHealthCheck
{
    private readonly RepositoryContext _repositoryContext;

    public StaleDeliveryHealthCheck(RepositoryContext repositoryContext)
    {
        _repositoryContext = repositoryContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var staleDeliveryCount = await _repositoryContext.WebhookDeliveries
                                            .CountAsync(x => x.DeliveryStatus == Core.Constants.WebhookDeliveryStatus.Processing && x.LockedUntil.HasValue && x.LockedUntil < DateTime.UtcNow, cancellationToken);
            return staleDeliveryCount > 0 ?
                new HealthCheckResult(status: HealthStatus.Degraded, description: $"{staleDeliveryCount} deliveries stuck in Processing status beyond their lease — reclaim worker may be delayed.") :
                new HealthCheckResult(status: HealthStatus.Healthy, description: "No delivery is stuck in processing state.");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(status: HealthStatus.Unhealthy, exception: ex, description: "An error occurred while  getting stale deliveries record.");
        }
    }
}