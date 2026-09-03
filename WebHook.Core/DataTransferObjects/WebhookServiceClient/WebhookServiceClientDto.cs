namespace WebHook.Core.DataTransferObjects.WebhookServiceClient;

public record class WebhookServiceClientDto
{
    public Guid Id { get; set; }
    public string ServiceClientName { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public bool ActiveStatus { get; set; }
    public IReadOnlyList<string> SubscribedCatalogs { get; set; } = [];
}
