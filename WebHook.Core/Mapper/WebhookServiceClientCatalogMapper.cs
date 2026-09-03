using System.Linq.Expressions;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;
using WebHook.Core.Entities;

namespace WebHook.Core.Mapper;

public static class WebhookServiceClientCatalogMapper
{
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