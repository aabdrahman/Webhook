namespace WebHook.Core.DataTransferObjects.WebhookDelivery;

public class WebhookDeliveryProcessorMetadataDto
{
    public Guid DeliveryId { get; set; }
    public string EncryptedSecret { get; set; }
    public string RaisedEventName { get; set; }
}
