using System.Linq.Expressions;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;
using WebHook.Core.Entities;

namespace WebHook.Core.Mapper;

/// <summary>
/// Maps <see cref="WebhookServiceClientEventCatalog"/> entities to DTOs.
/// </summary>
public static class WebhookServiceClientCatalogMapper
{
    /// <summary>
    /// A LINQ projection from <see cref="WebhookServiceClientEventCatalog"/> to
    /// <see cref="WebhookServiceClientCatalogDto"/> for EF Core queries. An assignment is active
    /// when it has no deactivation time.
    /// </summary>
    /// <returns>The projection expression.</returns>
    public static Expression<Func<WebhookServiceClientEventCatalog, WebhookServiceClientCatalogDto>> ToDtoExpression()
    {
        return wsCatalog => new WebhookServiceClientCatalogDto()
        {
            Id = wsCatalog.Id,
            CatalogName = wsCatalog.eventCatalog.NormalizedEventName,
            ServiceClientId = wsCatalog.ServiceClientId,
            IsActive = !wsCatalog.DeactivatedAt.HasValue
        };
    }
}