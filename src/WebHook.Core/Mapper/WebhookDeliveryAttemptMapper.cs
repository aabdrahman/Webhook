using System.Linq.Expressions;
using WebHook.Core.DataTransferObjects.WebhookDelivery;
using WebHook.Core.Entities;

namespace WebHook.Core.Mapper;

/// <summary>
/// Maps <see cref="WebhookDeliveryAttempt"/> entities to DTOs.
/// </summary>
public static class WebhookDeliveryAttemptMapper
{
    /// <summary>
    /// A LINQ projection from <see cref="WebhookDeliveryAttempt"/> to <see cref="WebhookDeliveryAttemptDto"/> for EF Core queries.
    /// </summary>
    /// <returns>The projection expression.</returns>
    public static Expression<Func<WebhookDeliveryAttempt, WebhookDeliveryAttemptDto>> ToDtoExpression()
    {
        return deliveryAttempt => new WebhookDeliveryAttemptDto()
        {
            HttpResponse = deliveryAttempt.HttpResponse,
            HttpDuration = deliveryAttempt.Duration,
            HttpStatusCode = deliveryAttempt.HttpResponseCode,
            AttemptedAt = deliveryAttempt.AttemptedAt
        };
    }
}