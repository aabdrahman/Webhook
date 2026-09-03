namespace WebHook.Core.DataTransferObjects.WebhookSubscription;

public record class WebhookSubscriptionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
    public string SecretKey { get; set; }
    public IReadOnlyList<string> SubscribedFields { get; set; } = [];
    public IReadOnlyList<string> SubscribedEvents { get; set; } = [];
}
