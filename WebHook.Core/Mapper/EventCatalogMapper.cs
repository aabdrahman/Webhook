using System.Linq.Expressions;
using WebHook.Core.DataTransferObjects.WebhookEventCatalog;
using WebHook.Core.Entities;

namespace WebHook.Core.Mapper;

/// <summary>
/// Provides mapping functionality between webhook event catalog entities and their DTO representations.
/// Includes entity-to-DTO, DTO-to-entity conversions, and expression-based projections for efficient querying.
/// </summary>
public static class EventCatalogMapper
{
    /// <summary>
    /// Converts a <see cref="CreateEventCatalogDto"/> into a <see cref="WebHookEventCatalog"/> entity.
    /// </summary>
    /// <param name="createEventCatalog">
    /// The DTO containing data required to create a webhook event catalog.
    /// </param>
    /// <returns>
    /// A new <see cref="WebHookEventCatalog"/> entity populated from the provided DTO.
    /// </returns>
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
    /// <summary>
    /// Converts a <see cref="WebHookEventCatalog"/> entity into an <see cref="EventCatalogDto"/>.
    /// </summary>
    /// <param name="webHookEventCatalog">
    /// The webhook event catalog entity to convert.
    /// </param>
    /// <returns>
    /// A <see cref="EventCatalogDto"/> representing the mapped entity.
    /// </returns>
    public static EventCatalogDto ToDto(this WebHookEventCatalog webHookEventCatalog)
    {
        return new EventCatalogDto()
        {
            Id = webHookEventCatalog.Id,
            AvailableFields = webHookEventCatalog.AvailableFields.Keys.ToList(),
            Description = webHookEventCatalog.Description,
            EventCatalogName = webHookEventCatalog.EventName,
            IsActive = webHookEventCatalog.IsActive
        };
    }
    /// <summary>
    /// Provides a LINQ expression for projecting <see cref="WebHookEventCatalog"/> entities
    /// into <see cref="EventCatalogDto"/> objects.
    /// This is optimized for database queries using EF Core projection.
    /// </summary>
    /// <returns>
    /// An expression tree that maps <see cref="WebHookEventCatalog"/> to <see cref="EventCatalogDto"/>.
    /// </returns>
    public static Expression<Func<WebHookEventCatalog, EventCatalogDto>> ToDtoExpression() 
    {
        return webHookEventCatalog => new EventCatalogDto()
        {
            Id = webHookEventCatalog.Id,
            AvailableFields = webHookEventCatalog.AvailableFields.Keys.ToList(),
            Description = webHookEventCatalog.Description,
            EventCatalogName = webHookEventCatalog.EventName,
            IsActive = webHookEventCatalog.IsActive
        };
    }
}
