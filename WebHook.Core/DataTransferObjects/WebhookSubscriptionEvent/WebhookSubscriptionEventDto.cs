namespace WebHook.Core.DataTransferObjects.WebhookSubscriptionEvent;

public record class WebhookSubscriptionEventDto
{
    public Guid SubscriptionId {  get; init; }

    public string SubscriptionName { get; init; }
}
