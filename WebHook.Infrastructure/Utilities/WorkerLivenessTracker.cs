namespace WebHook.Infrastructure.Utilities;

public sealed class WorkerLivenessTracker
{
    private readonly TimeSpan _timeSpan;
    private DateTimeOffset _lastHeartbeat = DateTimeOffset.UtcNow;

    public WorkerLivenessTracker(TimeSpan timeSpan)
    {
        _timeSpan = timeSpan;
    }

    public void UpdateHeartBeat() => 
        _lastHeartbeat = DateTimeOffset.UtcNow;

    public (bool, DateTimeOffset) CheckActiveStatus() => 
        ((DateTimeOffset.UtcNow - _lastHeartbeat) < _timeSpan, _lastHeartbeat);
}
