namespace WebHook.Core.Entities.ConfigurationModels;

public class EmailProcessorWorkerConfiguration
{
    public int ProcessingIntervalInSeconds { get; set; }
    public int ProcessingDelayInMilliSeconds { get; set; }
}
