namespace WebHook.Core.EventContracts.Events;

/// <summary>
/// In-process message signalling that a webhook event has been persisted and is
/// ready to be fanned out into deliveries.
/// </summary>
/// <param name="createdEventId">The ID of the persisted <see cref="Entities.WebhookEvent"/>.</param>
public record EventRaised
(Guid createdEventId);
