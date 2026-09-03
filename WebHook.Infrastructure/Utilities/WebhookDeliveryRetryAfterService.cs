namespace WebHook.Infrastructure.Utilities;

public sealed class WebhookDeliveryRetryAfterService
{
    public async Task<DateTimeOffset> GetRetryAfter(DateTimeOffset lastAttemptedTime, int totalAttemptedCount)
    {
        await Task.Delay(1);
        int secondsToAdd = totalAttemptedCount * 60;
        return lastAttemptedTime.AddSeconds(secondsToAdd);
    }
}
