using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.CustomHealthChecks;

public sealed class DeadLetterQueuedHealthCheck : IHealthCheck
{
    private readonly RepositoryContext _repositoryContext;

    public DeadLetterQueuedHealthCheck(RepositoryContext repositoryContext)
    {
        _repositoryContext = repositoryContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            int deadLetterItems = await _repositoryContext.WebhookDeadLetterQueues.CountAsync(x => !x.RetriedAt.HasValue);

            if (deadLetterItems > 10)
            {
                return new HealthCheckResult(status: HealthStatus.Unhealthy, description: $"Callback urls are failing. Total dead letter items: {deadLetterItems}");
            }

            return new HealthCheckResult(status: HealthStatus.Healthy, description: $"Dead letter items are minimal. System pushing deliveries to callback urls successfully. Total unresolved items:{deadLetterItems}");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(status: HealthStatus.Unhealthy, exception: ex, description: "An error occured while getting dead letetr items.");
        }
    }
}