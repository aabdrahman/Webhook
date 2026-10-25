using System.Linq.Expressions;
using WebHook.Core.DataTransferObjects.WebhookSubscription;
using WebHook.Core.Entities;

namespace WebHook.Core.Mapper;

/// <summary>
/// Maps between <see cref="WebhookSubscription"/> entities and their DTOs.
/// </summary>
public static class WebhookSubscriptionMapper
{
    /// <summary>
    /// A LINQ projection from <see cref="WebhookSubscription"/> to <see cref="WebhookSubscriptionDto"/>
    /// for EF Core queries. Only active event links are included.
    /// </summary>
    /// <remarks>
    /// <see cref="WebhookSubscriptionDto.SecretKey"/> is the stored ciphertext; callers must decrypt or clear it.
    /// </remarks>
    /// <returns>The projection expression.</returns>
    public static Expression<Func<WebhookSubscription, WebhookSubscriptionDto>> ToDtoExpression()
    {
        return wbSubscription => new WebhookSubscriptionDto()
        {
            CreatedDate = wbSubscription.CreatedAt,
            Id = wbSubscription.Id,
            Name = wbSubscription.Name,
            SecretKey = wbSubscription.SecretKey,
            SubscribedFields = wbSubscription.SubscribedFields,
            SubscribedEvents = wbSubscription.WebhookEvents.Where(x => x.IsActive).Select(x => x.webHookEventCatalog.NormalizedEventName).ToList(),
            CallBackUrl = wbSubscription.CallbackUrl,
            IsActive = wbSubscription.IsActive
        };
    }

    /// <summary>
    /// Converts a loaded <see cref="WebhookSubscription"/> to a <see cref="WebhookSubscriptionDto"/>.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="ToDtoExpression"/>, all loaded event links are included, active or not.
    /// <see cref="WebhookSubscription.WebhookEvents"/> and their catalog entries must be loaded.
    /// </remarks>
    /// <param name="wbSubscription">The subscription.</param>
    /// <returns>The DTO.</returns>
    public static WebhookSubscriptionDto ToDto(this WebhookSubscription wbSubscription)
    {
        return new WebhookSubscriptionDto()
        {
            CreatedDate = wbSubscription.CreatedAt,
            Id = wbSubscription.Id,
            Name = wbSubscription.Name,
            SecretKey = wbSubscription.SecretKey,
            SubscribedFields = wbSubscription.SubscribedFields,
            SubscribedEvents = wbSubscription.WebhookEvents.Select(x => x.webHookEventCatalog.NormalizedEventName).ToList(),
            CallBackUrl = wbSubscription.CallbackUrl,
            IsActive = wbSubscription.IsActive
        };
    }

    /// <summary>
    /// Creates an active <see cref="WebhookSubscription"/> from a creation request. The secret,
    /// event links, owner and contact email are set by the caller.
    /// </summary>
    /// <param name="createWebhookSubscription">The creation request.</param>
    /// <returns>A new, unsaved subscription.</returns>
    public static WebhookSubscription ToEntity(this CreateWebhookSubscriptionDto createWebhookSubscription)
    {
        return new WebhookSubscription()
        {
            IsActive = true,
            Name = createWebhookSubscription.SubscriberName,
            CallbackUrl = createWebhookSubscription.CallBackUrl,
            SubscribedFields = createWebhookSubscription.SubscribedFields
        };
    }
}
