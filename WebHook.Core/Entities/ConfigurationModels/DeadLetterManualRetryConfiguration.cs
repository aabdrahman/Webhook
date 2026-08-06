namespace WebHook.Core.Entities.ConfigurationModels;

/// <summary>
/// Defines the configuration settings that govern manual retries
/// of deliveries that have been moved to the dead-letter state.
/// </summary>
public class DeadLetterManualRetryConfiguration
{
    /// <summary>
    /// Gets or sets the maximum number of retry cycles that a delivery
    /// can undergo, including its initial retry cycle.
    /// This limit prevents an administrator from manually retrying
    /// a dead-lettered delivery indefinitely.
    /// </summary>
    public int MaximumRetryCycle { get; set; }
}