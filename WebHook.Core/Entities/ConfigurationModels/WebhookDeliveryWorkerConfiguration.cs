namespace WebHook.Core.Entities.ConfigurationModels;

public class WebhookDeliveryWorkerConfiguration
{
    public int DeliveryProcessorIntervalSeconds { get; set; }
    public int TotalBatchSize { get; set; }
    public double DeliveryLockDuration { get; set; }
}
