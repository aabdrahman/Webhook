namespace WebHook.Core.Entities.ConfigurationModels;

/// <summary>
/// Settings for retrying failed deliveries and releasing stale claims, used by the
/// <c>RetryPendingDeliveriesWorker</c> and <c>StaleClaimedDeliverReleaseWorker</c>.
/// Bound from configuration.
/// </summary>
public class RetryDeliveresAfterFailedConfiguration
{
    /// <summary>
    /// The HTTP duration, in milliseconds, above which a retry attempt is treated as a slow
    /// endpoint and an escalation email is sent to the subscription's contact.
    /// </summary>
    public long ThresholdDuration { get; set; }
    /// <summary>
    /// The maximum number of attempts per retry cycle. A failed delivery whose
    /// <c>RetryCount</c> exceeds this value multiplied by its current <c>RetryCycle</c>
    /// is moved to the dead-letter queue.
    /// </summary>
    public int MaximumAttendedCount { get; set; }
    /// <summary>
    /// The number of failed deliveries to process in a batch. This ensures that the system does not process too much items at once and can process intermittently.
    /// </summary>
    public int TotalBatchSize { get; set; }
    /// <summary>
    /// How long, in seconds, a claimed delivery stays locked to a worker. Also used by the
    /// stale delivery release worker: a lock older than this is treated as abandoned.
    /// </summary>
    public double DeliveryLockDuration { get; set; }
    /// <summary>
    /// How often (in seconds) the <c>StaleClaimedDeliverReleaseWorker</c>
    /// ticks to scan for and release stale locked deliveries.
    /// Default: 120 seconds.
    /// </summary>
    public int StaleDeliveryReleaseIntervalSeconds { get; set; } = 120;

    /// <summary>
    /// How often (in seconds) the <c>RetryPendingDeliveriesWorker</c>
    /// ticks to scan for and process failed deliveries.
    /// Default: 60 seconds.
    /// </summary>
    public int RetryFailedDeliveryIntervalSeconds { get; set; } = 60;
}
