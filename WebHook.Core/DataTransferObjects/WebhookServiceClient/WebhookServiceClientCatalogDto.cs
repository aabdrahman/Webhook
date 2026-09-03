namespace WebHook.Core.DataTransferObjects.WebhookServiceClient;

public class WebhookServiceClientCatalogDto
{
    public Guid Id { get; set; }
    public Guid ServiceClientId { get; set; }
    public string CatalogName { get; set; }
    public bool IsActive { get; set; }
}