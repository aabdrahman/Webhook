namespace WebHook.Core.Constants;

public enum WebhookDeliveryStatus
{
    Pending,
    Processing,
    Delivered,
    Failed,
    Retrying,
    DeadLetter
}