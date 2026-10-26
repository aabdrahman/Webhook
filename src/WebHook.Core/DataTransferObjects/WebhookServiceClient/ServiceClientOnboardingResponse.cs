namespace WebHook.Core.DataTransferObjects.WebhookServiceClient;

/// <summary>
/// The credentials issued when a service client is onboarded or its key is rotated.
/// </summary>
public record class ServiceClientOnboardingResponse
{
    /// <summary>The client ID to send in the <c>X-Client-Id</c> header.</summary>
    public string ClientId { get; set; } = string.Empty;
    /// <summary>
    /// The raw client key to send in the <c>X-Client-Key</c> header. Only a hash is
    /// stored, so this is the only time the key is shown.
    /// </summary>
    public string ClientKey { get; set; } = string.Empty;
    /// <summary>A reminder to store the key securely.</summary>
    public string Message { get; set; } = "Store your ClientKey securely. It will not be shown again.";
}
