using System.Linq.Expressions;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;
using WebHook.Core.Entities;

namespace WebHook.Core.Mapper;

public static class WebhookServiceClientMapper
{
    public static WebhookServiceClient ToEntity(this CreateServiceClientDto createServiceClient) =>
        new WebhookServiceClient()
        {
            ClientId = createServiceClient.ClientId.ToLowerInvariant(),
            ServiceClientName = createServiceClient.ServiceName.ToLower()
        };

    public static Expression<Func<WebhookServiceClient, WebhookServiceClientDto>> ToDtoExpression()
    {
        return wsClient => new WebhookServiceClientDto()
        {
            ActiveStatus = wsClient.IsActive,
            ServiceClientName = wsClient.ServiceClientName.ToLowerInvariant(),
            Id = wsClient.Id,
            ClientId = wsClient.ClientId,
            CreatedAt = wsClient.CreatedAt,
            SubscribedCatalogs = wsClient.EventCatalogs.Select(x => x.eventCatalog.NormalizedEventName.ToLower()).ToList()
        };
    }
}
