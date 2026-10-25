using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading.Channels;
using WebHook.Core.DataTransferObjects.EmailSender;

namespace WebHook.Infrastructure.CustomHealthChecks;

/// <summary>
/// Reports the backlog in the email channel. <see cref="HealthStatus.Unhealthy"/> when more than 12 emails are queued.
/// </summary>
public sealed class QueuedEmailHealthCheck : IHealthCheck
{
    private readonly Channel<EmailSenderDto> _emailSenderChannel;

    /// <summary>
    /// Initialises a new instance of the <see cref="QueuedEmailHealthCheck"/> class.
    /// </summary>
    /// <param name="emailSenderChannel">The email channel to inspect.</param>
    public QueuedEmailHealthCheck(Channel<EmailSenderDto> emailSenderChannel)
    {
        _emailSenderChannel = emailSenderChannel;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            int count = _emailSenderChannel.Reader.Count;

            if (count > 12)
            {
                return Task.FromResult(new HealthCheckResult(status: HealthStatus.Unhealthy, description: $"Queued mails are growing in the channel. Current Count: {count}"));
            }

            return Task.FromResult(new HealthCheckResult(status: HealthStatus.Healthy, description: "The channel mail items are processed successfully. No stale pending items in channel."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HealthCheckResult(status: HealthStatus.Degraded, description: "An error occurred while reading the count of queued eamil items in channel.", exception: ex));
        }
    }
}
