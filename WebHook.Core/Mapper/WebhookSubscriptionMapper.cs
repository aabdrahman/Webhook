using WebHook.Core.DataTransferObjects.WebhookSubscription;
using WebHook.Core.Entities;

namespace WebHook.Core.Mapper;

public static class WebhookSubscriptionMapper
{
    public static Func<WebhookSubscription, WebhookSubscriptionDto> ToDtoExpression()
    {
        return wbSubscription => new WebhookSubscriptionDto()
        {
            CreatedDate = wbSubscription.CreatedAt,
            Id = wbSubscription.Id,
            Name = wbSubscription.Name,
            SecretKey = wbSubscription.SecretKey,
            SubscribedFields = wbSubscription.SubscribedFields,
            SubscribedEvents = wbSubscription.WebhookEvents.Select(x => x.webHookEventCatalog.NormalizedEventName).ToList()
        };
    }

    public static WebhookSubscriptionDto ToDto(this WebhookSubscription wbSubscription)
    {
        return new WebhookSubscriptionDto()
        {
            CreatedDate = wbSubscription.CreatedAt,
            Id = wbSubscription.Id,
            Name = wbSubscription.Name,
            SecretKey = wbSubscription.SecretKey,
            SubscribedFields = wbSubscription.SubscribedFields,
            SubscribedEvents = wbSubscription.WebhookEvents.Select(x => x.webHookEventCatalog.NormalizedEventName).ToList()
        };
    }

    public static WebhookSubscription ToEntity(this CreateWebhookSubscriptionDto createWebhookSubscription)
    {
        return new WebhookSubscription()
        {
            IsActive = true,
            Name = createWebhookSubscription.SubscriberName,
            CallbackUrl = createWebhookSubscription.CallBackUrl,
            SubscribedFields = createWebhookSubscription.SubscribedFields
        };
    }
}
