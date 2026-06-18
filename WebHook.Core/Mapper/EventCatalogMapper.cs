using System.Linq.Expressions;
using WebHook.Core.DataTransferObjects.WebhookEventCatalog;
using WebHook.Core.Entities;

namespace WebHook.Core.Mapper;

public static class EventCatalogMapper
{
    public static WebHookEventCatalog ToEntity(this CreateEventCatalogDto createEventCatalog)
    {
        return new WebHookEventCatalog()
        {
            AvailableFields = createEventCatalog.AvailableFields,
            EventName = createEventCatalog.EventCatalogName,
            Description = createEventCatalog.Description ?? string.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };
    }

    public static EventCatalogDto ToDto(this WebHookEventCatalog webHookEventCatalog)
    {
        return new EventCatalogDto()
        {
            Id = webHookEventCatalog.Id,
            AvailableFields = webHookEventCatalog.AvailableFields,
            Description = webHookEventCatalog.Description,
            EventCatalogName = webHookEventCatalog.EventName,
            IsActive = webHookEventCatalog.IsActive
        };
    }

    public static Expression<Func<WebHookEventCatalog, EventCatalogDto>> ToDtoExpression() 
    {
        return webHookEventCatalog => new EventCatalogDto()
        {
            Id = webHookEventCatalog.Id,
            AvailableFields = webHookEventCatalog.AvailableFields,
            Description = webHookEventCatalog.Description,
            EventCatalogName = webHookEventCatalog.EventName,
            IsActive = webHookEventCatalog.IsActive
        };
    }
}
