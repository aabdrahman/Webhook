namespace WebHook.Core.DataTransferObjects.WebhookServiceClient;

/// <summary>
/// Read model of an onboarded service client.
/// </summary>
public record class WebhookServiceClientDto
{
    /// <summary>The internal ID of the service client.</summary>
    public Guid Id { get; set; }
    /// <summary>The service's display name.</summary>
    public string ServiceClientName { get; set; } = string.Empty;
    /// <summary>The client ID sent in the <c>X-Client-Id</c> header.</summary>
    public string ClientId { get; set; } = string.Empty;
    /// <summary>When the client was onboarded.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Whether the client is active. Inactive clients cannot publish events.</summary>
    public bool ActiveStatus { get; set; }
    /// <summary>The names of the event types the client may publish.</summary>
    public IReadOnlyList<string> SubscribedCatalogs { get; set; } = [];
}
