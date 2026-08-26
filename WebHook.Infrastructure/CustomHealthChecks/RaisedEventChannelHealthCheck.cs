using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading.Channels;
using WebHook.Core.EventContracts.Events;

namespace WebHook.Infrastructure.CustomHealthChecks;

public sealed class RaisedEventChannelHealthCheck : IHealthCheck
{
    private readonly Channel<EventRaised> _eventRaisedChannel;

    public RaisedEventChannelHealthCheck(Channel<EventRaised> eventRaisedChannel)
    {
        _eventRaisedChannel = eventRaisedChannel;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            int count = _eventRaisedChannel.Reader.Count;

            if (count > 10)
            {
                return Task.FromResult(new HealthCheckResult(status: HealthStatus.Unhealthy, description: $"Queued raised events are growing in the raised events channel. Current Count: {count}"));
            }

            return Task.FromResult(new HealthCheckResult(status: HealthStatus.Healthy, description: "The channel items are processed successfully. No stale pending items in channel."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HealthCheckResult(status: HealthStatus.Degraded, description: "An error occurred while reading the count of raised items in channel.", exception: ex));
        }
    }
}
