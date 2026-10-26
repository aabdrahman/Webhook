using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.CustomHealthChecks;

/// <summary>
/// Reports how many dead-letter entries are awaiting a manual retry.
/// <see cref="HealthStatus.Unhealthy"/> when there are more than 10.
/// </summary>
public sealed class DeadLetterQueuedHealthCheck : IHealthCheck
{
    private readonly RepositoryContext _repositoryContext;

    /// <summary>
    /// Initialises a new instance of the <see cref="DeadLetterQueuedHealthCheck"/> class.
    /// </summary>
    /// <param name="repositoryContext">The database context.</param>
    public DeadLetterQueuedHealthCheck(RepositoryContext repositoryContext)
    {
        _repositoryContext = repositoryContext;
    }

    /// <inheritdoc />
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
