namespace WebHook.Core.Entities.ConfigurationModels;

/// <summary>
/// Settings for the <c>WebhookDeliveryProcessorWorker</c>, which makes the first delivery
/// attempt for pending deliveries. Bound from configuration.
/// </summary>
public class WebhookDeliveryWorkerConfiguration
{
    /// <summary>
    /// How often (in seconds) the <c>WebhookDeliveryProcessorWorker</c>
    /// ticks to scan for and process deliveries the first time.
    /// </summary>
    public int DeliveryProcessorIntervalSeconds { get; set; }
    /// <summary>
    /// The maximum number of pending deliveries claimed and processed per tick.
    /// </summary>
    public int TotalBatchSize { get; set; }
    /// <summary>
    /// How long, in seconds, a claimed delivery stays locked to the worker that claimed it.
    /// </summary>
    public double DeliveryLockDuration { get; set; }
    /// <summary>
    /// The HTTP duration, in milliseconds, above which an attempt is treated as a slow
    /// endpoint and an escalation email is sent to the subscription's contact.
    /// </summary>
    public long ThresholdDuration { get; set; }
}
