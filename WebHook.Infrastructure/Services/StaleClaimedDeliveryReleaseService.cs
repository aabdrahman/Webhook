using Microsoft.EntityFrameworkCore;
using Serilog;
using WebHook.Core.Constants;
using WebHook.Core.Entities;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

/// <summary>
/// Releases webhook deliveries whose distributed locks have expired without
/// being committed — typically because the worker that claimed them crashed
/// or was forcefully terminated mid-flight.
/// </summary>
/// <remarks>
/// <para>
/// When a worker picks up a <see cref="WebhookDelivery"/> for processing it
/// sets <c>LockedBy</c> (worker identity) and <c>LockedUntil</c> (expiry time)
/// on the record. If the worker completes successfully it clears these fields
/// and commits. If the worker crashes before committing, the lock fields remain
/// set indefinitely, preventing any other worker from picking up the delivery.
/// </para>
/// <para>
/// This service identifies such stale-locked deliveries — records in
/// <see cref="WebhookDeliveryStatus.Processing"/> whose <c>LockedUntil</c>
/// is older than the configured <c>lockDurationSeconds</c> threshold — and
/// releases them by:
/// <list type="number">
///   <item><description>Clearing <c>LockedBy</c> and <c>LockedUntil</c>.</description></item>
///   <item><description>Resetting <c>DeliveryStatus</c> to <see cref="WebhookDeliveryStatus.Pending"/> so the delivery processor worker can pick it up again.</description></item>
///   <item><description>Incrementing <c>RetryCount</c> to reflect that an attempt was made even though it did not complete.</description></item>
/// </list>
/// </para>
/// <para>
/// This service is designed to be called from a
/// <see cref="Microsoft.Extensions.Hosting.BackgroundService"/> on a periodic
/// timer (e.g. every 5 minutes). It does not perform delivery itself.
/// </para>
/// </remarks>
public sealed class StaleClaimedDeliveryReleaseService
{
    private readonly RepositoryContext _repositoryContext;

    private ILogger _logger;
    private const string _className = "ClassName";
    private const string _methodName = "MethodName";

    /// <summary>
    /// Initializes a new instance of <see cref="StaleClaimedDeliveryReleaseService"/>.
    /// </summary>
    /// <param name="repositoryContext">
    /// The EF Core database context used to query and update
    /// <see cref="WebhookDelivery"/> records.
    /// </param>
    public StaleClaimedDeliveryReleaseService(RepositoryContext repositoryContext)
    {
        _repositoryContext = repositoryContext;
        _logger = Log.ForContext(_className, nameof(StaleClaimedDeliveryReleaseService));
    }

    /// <summary>
    /// Scans for stale-locked webhook deliveries and releases them back to
    /// <see cref="WebhookDeliveryStatus.Pending"/> so they can be reprocessed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A delivery is considered stale when ALL of the following are true:
    /// <list type="bullet">
    ///   <item><description><c>LockedBy</c> is not null or empty — the delivery was claimed by a worker.</description></item>
    ///   <item><description><c>LockedUntil</c> has a value — a lock expiry was set.</description></item>
    ///   <item><description><c>LockedUntil</c> is older than <c>DateTimeOffset.UtcNow - lockDurationSeconds</c> — the lock has expired.</description></item>
    ///   <item><description><c>DeliveryStatus</c> is <see cref="WebhookDeliveryStatus.Processing"/> — the delivery was in-flight when the worker died.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// For each stale delivery the following fields are updated atomically in
    /// a single <c>SaveChangesAsync</c> call:
    /// <list type="bullet">
    ///   <item><description><c>LockedBy</c> → <see langword="null"/></description></item>
    ///   <item><description><c>LockedUntil</c> → <see langword="null"/></description></item>
    ///   <item><description><c>DeliveryStatus</c> → <see cref="WebhookDeliveryStatus.Pending"/></description></item>
    ///   <item><description><c>RetryCount</c> → incremented by 1</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="lockDurationSeconds">
    /// The number of seconds after <c>LockedUntil</c> that must have elapsed
    /// before a delivery is considered stale. Defaults to 600 seconds (10 minutes).
    /// A lower value releases locks faster but risks releasing deliveries that
    /// are still legitimately in-flight on a slow worker.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the asynchronous
    /// operation before it completes.
    /// </param>
    /// <returns>
    /// The number of stale deliveries that were released.
    /// </returns>
    public async Task<int> ProcessStaleDeliveriesAsync(double lockDurationSeconds = 600, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(ProcessStaleDeliveriesAsync));

        _logger.Information("Begin processing stale locked deliveries release.Lock duration threshold: {0}s", lockDurationSeconds);

        try
        {
            DateTimeOffset thresholdTime = DateTimeOffset.UtcNow.AddSeconds(-lockDurationSeconds);

            List<WebhookDelivery> itemsToRelease = await _repositoryContext.WebhookDeliveries
                                                                                .Where(wd => !string.IsNullOrEmpty(wd.LockedBy)
                                                                                          && wd.LockedUntil.HasValue
                                                                                          && thresholdTime > wd.LockedUntil.Value
                                                                                          && wd.DeliveryStatus == WebhookDeliveryStatus.Processing)
                                                                                .ToListAsync(ct);

            if (!itemsToRelease.Any())
            {
                _logger.Information("No stale locked deliveries found to release.......");
                return 0;
            }

            _logger.Warning("{0} stale locked delivery/deliveries found. Releasing...", itemsToRelease.Count);

            foreach (var deliveryItem in itemsToRelease)
            {
                _logger.Warning("Releasing stale delivery {0} — LockedBy: {1}, LockedUntil: {2}, RetryCount: {3}", deliveryItem.Id, deliveryItem.LockedBy, deliveryItem.LockedUntil, deliveryItem.RetryCount);

                // Clear the lock fields so no worker considers this delivery in-flight by setting the lockedBy and lockedUntil to null and status to failed.
                deliveryItem.LockedBy = null;
                deliveryItem.LockedUntil = null;
                deliveryItem.DeliveryStatus = WebhookDeliveryStatus.Failed;

                deliveryItem.RetryCount++;
            }

            await _repositoryContext.SaveChangesAsync(ct);

            _logger.Information("{0} stale delivery/deliveries successfully released to Pending.", itemsToRelease.Count);

            return itemsToRelease.Count;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.Warning("ProcessStaleDeliveriesAsync was cancelled.");
            return 0;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while processing stale locked deliveries.");
            return 0;
        }
    }
}