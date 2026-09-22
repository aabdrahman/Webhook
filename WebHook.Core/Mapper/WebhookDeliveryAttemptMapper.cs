using System.Linq.Expressions;
using WebHook.Core.DataTransferObjects.WebhookDelivery;
using WebHook.Core.Entities;

namespace WebHook.Core.Mapper;

public static class WebhookDeliveryAttemptMapper
{
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