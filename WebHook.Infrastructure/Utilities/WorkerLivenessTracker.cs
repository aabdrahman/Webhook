namespace WebHook.Infrastructure.Utilities;

/// <summary>
/// Records the time of the most recent background worker heartbeat, shared by all workers.
/// </summary>
/// <remarks>
/// A single timestamp is kept for every worker, so the tracker reports active as long as any
/// one of them is still beating.
/// </remarks>
public sealed class WorkerLivenessTracker
{
    private readonly TimeSpan _timeSpan;
    private DateTimeOffset _lastHeartbeat = DateTimeOffset.UtcNow;

    /// <summary>
    /// Initialises a new instance of the <see cref="WorkerLivenessTracker"/> class.
    /// </summary>
    /// <param name="timeSpan">How long after the last heartbeat the workers are still considered active.</param>
    public WorkerLivenessTracker(TimeSpan timeSpan)
    {
        _timeSpan = timeSpan;
    }

    /// <summary>Records a heartbeat at the current time.</summary>
    public void UpdateHeartBeat() => 
        _lastHeartbeat = DateTimeOffset.UtcNow;

    /// <summary>
    /// Checks whether a heartbeat was recorded within the configured time span.
    /// </summary>
    /// <returns>Whether the workers are active, and the time of the last heartbeat.</returns>
    public (bool, DateTimeOffset) CheckActiveStatus() => 
        ((DateTimeOffset.UtcNow - _lastHeartbeat) < _timeSpan, _lastHeartbeat);
}
