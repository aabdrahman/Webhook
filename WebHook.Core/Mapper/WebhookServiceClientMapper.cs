using System.Linq.Expressions;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;
using WebHook.Core.Entities;

namespace WebHook.Core.Mapper;

/// <summary>
/// Maps between <see cref="WebhookServiceClient"/> entities and their DTOs.
/// </summary>
public static class WebhookServiceClientMapper
{
    /// <summary>
    /// Creates a <see cref="WebhookServiceClient"/> from an onboarding request, with the client ID
    /// and service name in lower case. The key and event type assignments are set by the caller.
    /// </summary>
    /// <param name="createServiceClient">The onboarding request.</param>
    /// <returns>A new, unsaved service client.</returns>
    public static WebhookServiceClient ToEntity(this CreateServiceClientDto createServiceClient) =>
        new WebhookServiceClient()
        {
            ClientId = createServiceClient.ClientId.ToLowerInvariant(),
            ServiceClientName = createServiceClient.ServiceName.ToLower()
        };

    /// <summary>
    /// A LINQ projection from <see cref="WebhookServiceClient"/> to <see cref="WebhookServiceClientDto"/> for EF Core queries.
    /// </summary>
    /// <returns>The projection expression.</returns>
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
