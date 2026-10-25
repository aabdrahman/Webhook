namespace WebHook.Core.Entities.ConfigurationModels;

/// <summary>
/// Settings for the <c>EmailProcessorWorker</c>, which sends queued notification emails.
/// Bound from configuration.
/// </summary>
public class EmailProcessorWorkerConfiguration
{
    /// <summary>The period, in seconds, of the worker's timer.</summary>
    public int ProcessingIntervalInSeconds { get; set; }
    /// <summary>
    /// The delay, in milliseconds, applied before sending each queued email.
    /// Throttles the rate at which the SMTP server is called.
    /// </summary>
    public int ProcessingDelayInMilliSeconds { get; set; }
}
