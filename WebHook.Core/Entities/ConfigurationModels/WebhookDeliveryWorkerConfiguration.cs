namespace WebHook.Core.Entities.ConfigurationModels;

public class WebhookDeliveryWorkerConfiguration
{
    /// <summary>
    /// How often (in seconds) the <see cref="WebhookDeliveryProcessorWorker"/>
    /// ticks to scan for and process deliveries the first time.
    /// Default: 120 seconds.
    /// </summary>
    public int DeliveryProcessorIntervalSeconds { get; set; }
    /// <summary>
    /// This is the duration at which a delivery item is locked for in case exception occurs during delivery processing.
    /// </summary>
    public int TotalBatchSize { get; set; }
    /// <summary>
    /// This is the duration at which a delivery item is locked for in case exception occurs during delivery processing
    /// </summary>
    public double DeliveryLockDuration { get; set; }
}
