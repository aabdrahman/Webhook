namespace WebHook.Core.Constants;

public enum WebHookEventStatus
{
    Pending,
    Processing,
    Processed,
    PartiallyProcessed,
    Failed
}
