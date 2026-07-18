namespace WebHook.Core.Entities.ConfigurationModels;

public class RetryDeliveresAfterFailedConfiguration
{
    public long ThresholdDuration { get; set; }
    public int MaximumAttendedCount { get; set; }
    public int TotalBatchSize { get; set; }
}