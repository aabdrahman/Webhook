using System.Linq.Expressions;
using WebHook.Core.DataTransferObjects.WebhookEvent;
using WebHook.Core.Entities;

namespace WebHook.Core.Mapper;

/// <summary>
/// Maps between <see cref="WebhookEvent"/> entities and their DTOs.
/// </summary>
public static class WebhookEventMapper
{

    /// <summary>
    /// A LINQ projection from <see cref="WebhookEvent"/> to <see cref="WebhookEventDto"/> for EF Core queries.
    /// </summary>
    /// <returns>The projection expression.</returns>
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

    /// <summary>
    /// Converts a <see cref="WebhookEvent"/> to a <see cref="WebhookEventDto"/>.
    /// </summary>
    /// <param name="entity">The event.</param>
    /// <returns>The DTO, or <see langword="null"/> if <paramref name="entity"/> is <see langword="null"/>.</returns>
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


    /// <summary>
    /// Creates a <see cref="Constants.WebHookEventStatus.Pending"/> <see cref="WebhookEvent"/> from a
    /// publish request, with the event type in upper case.
    /// </summary>
    /// <param name="dto">The publish request.</param>
    /// <returns>A new, unsaved event, or <see langword="null"/> if <paramref name="dto"/> is <see langword="null"/>.</returns>
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
