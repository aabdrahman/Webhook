namespace WebHook.Core.Entities.ConfigurationModels;

public class EventRaisedWorkerConfiguration
{
    /// <summary>
    /// This specifies how often the <see cref="EventRaisedWorker"/> ticks to scan its channels for any raised events to process.
    /// Default: 5 seconds
    /// </summary>
    public int ProcessingIntervalInSeconds { get; set; } = 5;
}
