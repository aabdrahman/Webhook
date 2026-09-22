using System.Linq.Expressions;
using WebHook.Core.DataTransferObjects.WebhookDelivery;
using WebHook.Core.Entities;

namespace WebHook.Core.Mapper;

public static class WebhookDeliveryMapper
{
    public static Expression<Func<WebhookDelivery, WebhookDeliveryDto>> ToDtoExpression()
    {
        return delivery => new WebhookDeliveryDto()
        {
            Id = delivery.Id,
            EventType = delivery.webhookEvent.EventType,
            RetryCycle = delivery.RetryCycle,
            RetryCount = delivery.RetryCount,
            Status = delivery.DeliveryStatus.ToString(),
            AverageDuration = delivery.WebhookDeliveryAttempts.Average(x => x.Duration),
            SubscriberName = delivery.WebhookSubscriptionEvent.webhookSubscription.Name,
            CreatedAt = delivery.CreatedAt
        };
    }

    public static Expression<Func<WebhookDelivery, WebhookDeliveryDetailsDto>> ToDetailsExpression()
    {
        return delivery => new WebhookDeliveryDetailsDto()
        {
            Id = delivery.Id,
            EventType = delivery.webhookEvent.EventType,
            RetryCycle = delivery.RetryCycle,
            RetryCount = delivery.RetryCount,
            Status = delivery.DeliveryStatus.ToString(),
            AverageDuration = delivery.WebhookDeliveryAttempts.Average(x => x.Duration),
            SubscriberName = delivery.WebhookSubscriptionEvent.webhookSubscription.Name,
            CreatedAt = delivery.CreatedAt,
            CallBackUrl = delivery.CallBackUrl,
            DeliveredAt = delivery.DeliveredAt,
            NextRetryAt = delivery.NextRetryAt,
            RequestPayload = delivery.RequestPayload,
            TotalAttempts = delivery.WebhookDeliveryAttempts.Count,
            TotalFailedAttempts = delivery.WebhookDeliveryAttempts.Count(x => !x.HttpResponse.StartsWith("2"))
        };
    }
}
