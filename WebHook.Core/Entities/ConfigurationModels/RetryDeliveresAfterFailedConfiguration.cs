namespace WebHook.Core.Entities.ConfigurationModels;

public class RetryDeliveresAfterFailedConfiguration
{
    /// <summary>
    /// The duration threshold for when the system should escalate to contact email if the http call to callback url exceeds the value
    /// </summary>
    public long ThresholdDuration { get; set; }
    /// <summary>
    /// Ths is the maximum number of times a delivery is processed before marking it as a dead letter
    /// </summary>
    public int MaximumAttendedCount { get; set; }
    /// <summary>
    /// The number of failed deliveries to process in a batch. This ensures that the system does not process too much items at once and can process intermittently.
    /// </summary>
    public int TotalBatchSize { get; set; }
    /// <summary>
    /// This is the duration at which a delivery item is locked for in case exception occurs wduring delivery processing
    /// </summary>
    public double DeliveryLockDuration { get; set; }
    /// <summary>
    /// How often (in seconds) the <see cref="StaleClaimedDeliverRelseaseWorker"/>
    /// ticks to scan for and release stale locked deliveries.
    /// Default: 120 seconds.
    /// </summary>
    public int StaleDeliveryReleaseIntervalSeconds { get; set; } = 120;

    /// <summary>
    /// How often (in seconds) the <see cref="RetryPendingDeliveriesWorker"/>
    /// ticks to scan for and process failed deliveries.
    /// Default: 60 seconds.
    /// </summary>
    public int RetryFailedDeliveryIntervalSeconds { get; set; } = 60;
}

