using WebHook.Core.DataTransferObjects.ApplicationCache;
using WebHook.Core.DataTransferObjects.EmailSender;
using WebHook.Core.EventContracts.Events;

namespace WebHook.Core.EventContracts.Publishers;

/// <summary>
/// Queues work for the application's background workers through in-memory channels.
/// </summary>
/// <remarks>
/// Channels are not durable: items queued but not yet processed are lost on restart.
/// Raised events are recovered by the pending raised events worker, which re-queues
/// events still <c>Pending</c> in the database; queued emails are not recovered. Queued cache
/// entries are lost too, but the cache is a best-effort copy and the entries are re-created on demand.
/// </remarks>
public interface IApplicationPublisher
{
    /// <summary>
    /// Queues a raised event for the event fan-out worker.
    /// </summary>
    /// <param name="eventRaised">The message identifying the persisted event.</param>
    /// <param name="ct">A token to cancel the write.</param>
    /// <returns>A task that completes once the message is queued.</returns>
    Task QueueEventRaised(EventRaised @eventRaised, CancellationToken ct = default);
    /// <summary>
    /// Queues an email for the email processor worker.
    /// </summary>
    /// <param name="emailSenderItem">The email to send.</param>
    /// <param name="ct">A token to cancel the write.</param>
    /// <returns><see langword="true"/> if the email was queued; <see langword="false"/> if the channel is closed.</returns>
    Task<bool> QueueEmailEvent(EmailSenderDto emailSenderItem, CancellationToken ct = default);
    /// <summary>
    /// Queues a cache entry for the cache persistence worker, which writes it to the persistent cache table.
    /// </summary>
    /// <param name="cacheEvent">The key, JSON value and expiry to persist.</param>
    /// <param name="ct">A token to cancel the write.</param>
    /// <returns><see langword="true"/> if the entry was queued; <see langword="false"/> if the channel is closed.</returns>
    Task<bool> QueueItemToCache(ApplicationCacheEvent cacheEvent, CancellationToken ct = default);
}
