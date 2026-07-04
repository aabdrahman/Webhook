using System.Linq.Expressions;
using WebHook.Core.DataTransferObjects.WebhookEvent;
using WebHook.Core.Entities;

namespace WebHook.Core.Mapper;

public static class WebhookEventMapper
{

    public static Expression<Func<WebhookEvent, WebhookEventDto>> ToDtoExpression()
    {
        return e => new WebhookEventDto
        {
            Id = e.Id,
            EventType = e.EventType,
            PayLoad = e.PayLoad,
            Source = e.Source,
            CorrelationId = e.CorrelationId,
            Status = e.Status.ToString(),
            CreatedAt = e.CreatedAt,
            ProcessedAt = e.ProcessedAt
        };
    }

    public static WebhookEventDto ToDto(this WebhookEvent entity)
    {
        if (entity == null) return null;

        return new WebhookEventDto
        {
            Id = entity.Id,
            EventType = entity.EventType,
            PayLoad = entity.PayLoad,
            Source = entity.Source,
            CorrelationId = entity.CorrelationId,
            Status = entity.Status.ToString(),
            CreatedAt = entity.CreatedAt,
            ProcessedAt = entity.ProcessedAt
        };
    }


    public static WebhookEvent ToEntity(this CreateWebhookEventDto dto)
    {
        if (dto == null) return null;
        return new WebhookEvent
        {
            EventType = dto.EventType.ToUpper(),
            PayLoad = dto.PayLoad,
            Source = dto.Source,
            CorrelationId = dto.CorrelationId,
            Status = Constants.WebHookEventStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
