using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.CustomHealthChecks;

public sealed class PendingDeliveriesHealthCheck : IHealthCheck
{
    private readonly RepositoryContext _repositoryContext;


    public PendingDeliveriesHealthCheck(RepositoryContext repositoryContext)
    {
        _repositoryContext = repositoryContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            int pendingDeliveries = await _repositoryContext.WebhookDeliveries.CountAsync(x => x.DeliveryStatus == Core.Constants.WebhookDeliveryStatus.Pending, cancellationToken);

            return new HealthCheckResult(status: HealthStatus.Healthy, description: $"Total pending deliveries - {pendingDeliveries}");

        }
        catch (Exception ex)
        {
            return new HealthCheckResult(status: HealthStatus.Unhealthy, exception: ex, description: "An error occurred while getting the pending deliveries count.");
        }

    }
}
