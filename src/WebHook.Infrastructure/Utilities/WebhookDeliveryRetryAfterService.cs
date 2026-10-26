namespace WebHook.Infrastructure.Utilities;

/// <summary>
/// Calculates when a failed delivery should next be retried, using linear back-off.
/// </summary>
public sealed class WebhookDeliveryRetryAfterService
{
    /// <summary>
    /// Gets the next retry time: <paramref name="totalAttemptedCount"/> minutes after <paramref name="lastAttemptedTime"/>.
    /// </summary>
    /// <param name="lastAttemptedTime">When the last attempt was made.</param>
    /// <param name="totalAttemptedCount">The number of the attempt being scheduled.</param>
    /// <returns>The time the next attempt is due.</returns>
    public async Task<DateTimeOffset> GetRetryAfter(DateTimeOffset lastAttemptedTime, int totalAttemptedCount)
    {
        await Task.Delay(1);
        int secondsToAdd = totalAttemptedCount * 60;
        return lastAttemptedTime.AddSeconds(secondsToAdd);
    }
}
