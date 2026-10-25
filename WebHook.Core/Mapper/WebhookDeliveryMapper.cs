using System.Linq.Expressions;
using WebHook.Core.DataTransferObjects.WebhookDelivery;
using WebHook.Core.Entities;

namespace WebHook.Core.Mapper;

/// <summary>
/// Maps <see cref="WebhookDelivery"/> entities to DTOs.
/// </summary>
public static class WebhookDeliveryMapper
{
    /// <summary>
    /// A LINQ projection from <see cref="WebhookDelivery"/> to the <see cref="WebhookDeliveryDto"/> summary for EF Core queries.
    /// </summary>
    /// <returns>The projection expression.</returns>
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

    /// <summary>
    /// A LINQ projection from <see cref="WebhookDelivery"/> to <see cref="WebhookDeliveryDetailsDto"/> for EF Core queries.
    /// </summary>
    /// <remarks>
    /// <see cref="WebhookDeliveryDetailsDto.TotalFailedAttempts"/> counts attempts whose HTTP status code is not 2xx.
    /// </remarks>
    /// <returns>The projection expression.</returns>
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
            SubscriberName = delivery.WebhookSubscriptionEvent.webhookSubscription.Name ?? string.Empty,
            CreatedAt = delivery.CreatedAt,
            CallBackUrl = delivery.CallBackUrl,
            DeliveredAt = delivery.DeliveredAt,
            NextRetryAt = delivery.NextRetryAt,
            RequestPayload = delivery.RequestPayload,
            TotalAttempts = delivery.WebhookDeliveryAttempts.Count,
            TotalFailedAttempts = delivery.WebhookDeliveryAttempts.Count(x => !x.HttpResponseCode.StartsWith("2"))
        };
    }
}
