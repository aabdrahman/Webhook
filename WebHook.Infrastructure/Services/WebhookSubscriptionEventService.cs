using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookSubscriptionEvent;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

/// <summary>
/// Provides functionality for managing the webhook events associated with webhook subscriptions.
/// </summary>
/// <remarks>
/// This service implements the business logic for subscribing and unsubscribing webhook
/// subscriptions to event types, as well as retrieving the events currently associated
/// with a subscription.
///
/// The service validates subscriptions and events before performing operations,
/// prevents duplicate active subscriptions, supports reactivating previously
/// deleted subscriptions, and persists changes using the application's repository context.
/// </remarks>
public sealed class WebhookSubscriptionEventService : IWebhookSubscriptionEventService
{
    private readonly RepositoryContext _repositoryContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookSubscriptionEventService"/> class.
    /// </summary>
    /// <param name="repositoryContext">
    /// The repository context used to query and persist webhook subscription event data.
    /// </param>
    public WebhookSubscriptionEventService(RepositoryContext repositoryContext)
    {
        _repositoryContext = repositoryContext;
        _logger = Log.ForContext<WebhookSubscriptionEventService>().ForContext(_className, nameof(WebhookSubscriptionEventService));
    }


    private const string _className = "ClassName";
    private const string _methodName = "MethodName";

    private ILogger _logger;

    /// <summary>
    /// Retrieves all webhook events currently subscribed to by the specified webhook subscription.
    /// </summary>
    /// <param name="subscriptionId">
    /// The unique identifier of the webhook subscription.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> containing the collection of subscribed webhook
    /// events when the operation succeeds. If no subscriptions exist, a failure response
    /// with a <see cref="HttpStatusCode.NotFound"/> status is returned.
    /// </returns>
    /// <remarks>
    /// Only active event subscriptions associated with the specified webhook subscription
    /// are returned.
    /// </remarks>
    /// <exception cref="Exception">
    /// Exceptions are caught internally and returned as a failure response.
    /// </exception>
    public async Task<GenericResponse<IReadOnlyList<WebhookSubscriptionEventDto>>> GetSubscribedEventsAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        _logger = Log.ForContext(_methodName, nameof(GetSubscribedEventsAsync));

        try
        {
            _logger.Information("Fetching subscribed events for subscriptionId: {SubscriptionId}", subscriptionId);

            var subscribedEvents = await _repositoryContext.WebhookEventSubscriptions
                .Where(se => se.WebhookSubscriptionId == subscriptionId)
                .Select(se => new WebhookSubscriptionEventDto
                {
                    SubscriptionId = se.WebhookSubscriptionId,
                    SubscriptionName = se.webHookEventCatalog.NormalizedEventName
                })
                .ToListAsync(cancellationToken);

            if(!subscribedEvents.Any())
            {
                _logger.Warning("No subscribed events found for subscriptionId: {SubscriptionId}", subscriptionId);
                return GenericResponse<IReadOnlyList<WebhookSubscriptionEventDto>>.Failure(null, "No subscribed events found for the specified subscription.", HttpStatusCode.NotFound,
                                                        new ErrorDetail { ErrorMessage = "No subscribed events found.", ErrorTitle = "Not Found", ErrorDescription = $"The subscription with ID {subscriptionId} has no subscribed events." });
            }

            _logger.Information("Webhook Subscribed Events fetched successfully - {0}", subscribedEvents);

            return GenericResponse<IReadOnlyList<WebhookSubscriptionEventDto>>.Success(subscribedEvents, "Subscribed events fetched successfully.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred fetching subscribed events.");
            return GenericResponse<IReadOnlyList<WebhookSubscriptionEventDto>>.Failure(null, "An error occurred fetching subscribed events.", HttpStatusCode.InternalServerError,
                                                    new ErrorDetail { ErrorMessage = ex.Message, ErrorTitle = ex.GetType().Name, ErrorDescription = ex.InnerException?.Message ?? "" });

        }

    }

    /// <summary>
    /// Subscribes a webhook subscription to the specified webhook event.
    /// </summary>
    /// <param name="subscriptionId">
    /// The unique identifier of the webhook subscription.
    /// </param>
    /// <param name="eventName">
    /// The normalized name of the webhook event to subscribe to.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> indicating the outcome of the subscription request.
    /// </returns>
    /// <remarks>
    /// This operation:
    /// <list type="bullet">
    /// <item><description>Verifies that the webhook subscription exists.</description></item>
    /// <item><description>Verifies that the requested webhook event exists.</description></item>
    /// <item><description>Prevents duplicate active subscriptions.</description></item>
    /// <item><description>Reactivates an inactive subscription instead of creating a duplicate.</description></item>
    /// <item><description>Creates a new subscription when none exists.</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="Exception">
    /// Exceptions are caught internally and returned as a failure response.
    /// </exception>
    public async Task<GenericResponse<string>> SubscribeToEventAsync(Guid subscriptionId, string eventName, CancellationToken cancellationToken = default)
    {
        _logger = Log.ForContext(_methodName, nameof(SubscribeToEventAsync));

        try
        {
            _logger.Information("Subscribing to event: {EventName} for subscriptionId: {SubscriptionId}", eventName, subscriptionId);

            bool isSubscriptionExists = await _repositoryContext.WebhookSubscriptions
                                                                    .AsNoTracking()
                                                                    .AnyAsync(s => s.Id == subscriptionId, cancellationToken);

            if (!isSubscriptionExists)
            {
                _logger.Warning("Subscription with ID: {SubscriptionId} does not exist.", subscriptionId);
                return GenericResponse<string>.Failure(null, "Subscription does not exist.", HttpStatusCode.NotFound,
                                                        new ErrorDetail { ErrorMessage = "Subscription not found.", ErrorTitle = "Not Found", ErrorDescription = $"The subscription with ID {subscriptionId} does not exist." });
            }

            WebHookEventCatalog? eventCatalog = await _repositoryContext.WebHookEventCatalogs
                                                                            .AsNoTracking()
                                                                            .FirstOrDefaultAsync(e => e.NormalizedEventName == eventName.ToUpper(), cancellationToken);

            if (eventCatalog is null)
            {
                _logger.Warning("Event with name: {EventName} does not exist.", eventName);
                return GenericResponse<string>.Failure(null, "Event does not exist.", HttpStatusCode.BadRequest,
                                                        new ErrorDetail { ErrorMessage = "Event not found.", ErrorTitle = "Bad Request", ErrorDescription = $"The event with name {eventName} does not exist." });
            }

            WebhookSubscriptionEvent? alreadySubscribedEvent = await _repositoryContext.WebhookEventSubscriptions
                                                                            .IgnoreQueryFilters()
                                                                            .SingleOrDefaultAsync(se => se.WebhookSubscriptionId == subscriptionId && se.webHookEventCatalog.NormalizedEventName == eventName.ToUpper(), cancellationToken);

            if(alreadySubscribedEvent is not null && alreadySubscribedEvent.IsActive)
            {
                _logger.Warning("An active Subscription already exists for event: {EventName} and subscriptionId: {SubscriptionId}", eventName, subscriptionId);
                return GenericResponse<string>.Failure(null, "Subscription already exists for the specified event.", HttpStatusCode.Conflict,
                                                        new ErrorDetail { ErrorMessage = "Subscription already exists.", ErrorTitle = "Conflict", ErrorDescription = $"The subscription with ID {subscriptionId} is already subscribed to the event '{eventName}'." });
            }

            if (alreadySubscribedEvent is not null && !alreadySubscribedEvent.IsActive)
            {
                _logger.Information("Begin reactivating subscription for event: {EventName} and subscriptionId: {SubscriptionId}", eventName, subscriptionId);

                alreadySubscribedEvent.IsActive = true;
                alreadySubscribedEvent.DeletedAt = null;
                _repositoryContext.WebhookEventSubscriptions.Update(alreadySubscribedEvent);
            }
            else
            {

                _logger.Information("Creating new subscription for event: {EventName} and subscriptionId: {SubscriptionId}", eventName, subscriptionId);

                WebhookSubscriptionEvent newSubscriptionEvent = new()
                {
                    WebhookSubscriptionId = subscriptionId,
                    WebhookEventCatalogId = eventCatalog.Id,
                    CreatedAt = DateTimeOffset.UtcNow,
                    IsActive = true
                };

                await _repositoryContext.WebhookEventSubscriptions.AddAsync(newSubscriptionEvent, cancellationToken);
            }

            await _repositoryContext.SaveChangesAsync(cancellationToken);
            _logger.Information("Successfully subscribed to event: {EventName} for subscriptionId: {SubscriptionId}", eventName, subscriptionId);

            return GenericResponse<string>.Success(null, "Successfully subscribed to event.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while subscribing to event.");
            return GenericResponse<string>.Failure(null, "An error occurred while subscribing to event.", HttpStatusCode.InternalServerError,
                                                    new ErrorDetail { ErrorMessage = ex.Message, ErrorTitle = ex.GetType().Name, ErrorDescription = ex.InnerException?.Message ?? "" });
        }
    }

    /// <summary>
    /// Removes an event subscription from a webhook subscription.
    /// </summary>
    /// <param name="subscriptionId">
    /// The unique identifier of the webhook subscription.
    /// </param>
    /// <param name="eventName">
    /// The normalized name of the webhook event to remove.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> indicating whether the webhook subscription
    /// was successfully removed from the specified event.
    /// </returns>
    /// <remarks>
    /// This operation performs a soft delete by marking the subscription as inactive
    /// and recording the deletion timestamp. The subscription can be reactivated
    /// through a subsequent subscription request.
    /// </remarks>
    /// <exception cref="Exception">
    /// Exceptions are caught internally and returned as a failure response.
    /// </exception>
    public async Task<GenericResponse<string>> UnsubscribeFromEventAsync(Guid subscriptionId, string eventName, CancellationToken cancellationToken = default)
    {
        _logger = Log.ForContext(_methodName, nameof(UnsubscribeFromEventAsync));

        try
        {
            _logger.Information("Unsubscribing from event: {EventName} for subscriptionId: {SubscriptionId}", eventName, subscriptionId);

            WebhookSubscriptionEvent? subscriptionEvent = await _repositoryContext.WebhookEventSubscriptions
                                                        .SingleOrDefaultAsync(se => se.WebhookSubscriptionId == subscriptionId && se.webHookEventCatalog.NormalizedEventName == eventName.ToUpper(), cancellationToken);

            if(subscriptionEvent is null)
            {
                _logger.Warning("No subscription found for event: {EventName} and subscriptionId: {SubscriptionId}", eventName, subscriptionId);
                return GenericResponse<string>.Failure(null, "No subscription found for the specified event.", HttpStatusCode.NotFound,
                                                        new ErrorDetail { ErrorMessage = "Subscription not found.", ErrorTitle = "Not Found", ErrorDescription = $"The subscription with ID {subscriptionId} is not subscribed to the event '{eventName}'." });
            }

            subscriptionEvent.IsActive = false;
            subscriptionEvent.DeletedAt = DateTimeOffset.UtcNow;

            await _repositoryContext.SaveChangesAsync(cancellationToken);

            _logger.Information("Successfully unsubscribed from event: {EventName} for subscriptionId: {SubscriptionId}", eventName, subscriptionId);

            return GenericResponse<string>.Success(null, "Successfully unsubscribed from event.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while unsubscribing from an event");
            return GenericResponse<string>.Failure(null, "An error occurred while unsubscribing from an event.", HttpStatusCode.InternalServerError,
                                                    new ErrorDetail { ErrorMessage = ex.Message, ErrorTitle = ex.GetType().Name, ErrorDescription = ex.InnerException?.Message ?? "" });
        }
    }
}
