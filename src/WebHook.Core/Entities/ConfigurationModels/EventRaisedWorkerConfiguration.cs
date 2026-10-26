namespace WebHook.Core.Entities.ConfigurationModels;

/// <summary>
/// Settings for the <c>EventRaisedWorker</c>, which fans raised events out into deliveries.
/// Bound from configuration.
/// </summary>
public class EventRaisedWorkerConfiguration
{
    /// <summary>
    /// This specifies how often the <c>EventRaisedWorker</c> ticks to scan its channels for any raised events to process.
    /// Default: 5 seconds
    /// </summary>
    public int ProcessingIntervalInSeconds { get; set; } = 5;
}
