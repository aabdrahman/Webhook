namespace WebHook.Core.DataTransferObjects.WebhookServiceClient;

public record class ServiceClientOnboardingResponse
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientKey { get; set; } = string.Empty;
    public string Message { get; set; } = "Store your ClientKey securely. It will not be shown again.";
}
