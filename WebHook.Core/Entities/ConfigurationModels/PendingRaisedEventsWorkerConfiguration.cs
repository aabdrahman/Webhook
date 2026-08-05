namespace WebHook.Core.Entities.ConfigurationModels;

public class PendingRaisedEventsWorkerConfiguration
{
    /// <summary>
    /// Gets or sets the interval, in seconds, at which the
    /// <see cref="PendingRaisedEventsWorker"/> scans for pending raised events
    /// that require processing.
    /// <para>
    /// The worker performs a scan each time this interval elapses.
    /// </para>
    /// <para>
    /// Default value: 300 seconds (5 minutes).
    /// </para>
    /// </summary>
    public int PendingEventsWorkerIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets the threshold, in minutes, used to determine how long a
    /// pending raised event may remain unprocessed before it becomes eligible
    /// for another processing attempt.
    /// <para>
    /// This threshold is used to identify pending events that may have been
    /// missed during normal processing and need to be picked up again.
    /// </para>
    /// <para>
    /// Default value: 30 minutes.
    /// </para>
    /// </summary>
    public int PendingEventsThresholdMinutes { get; set; } = 30;
}
