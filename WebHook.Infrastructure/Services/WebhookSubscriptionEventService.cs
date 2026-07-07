using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookSubscriptionEvent;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

public sealed class WebhookSubscriptionEventService : IWebhookSubscriptionEventService
{
    private readonly RepositoryContext _repositoryContext;

    public WebhookSubscriptionEventService(RepositoryContext repositoryContext)
    {
        _repositoryContext = repositoryContext;
        _logger = Log.ForContext<WebhookSubscriptionEventService>().ForContext(_className, nameof(WebhookSubscriptionEventService));
    }


    private const string _className = "ClassName";
    private const string _methodName = "MethodName";

    private ILogger _logger;
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
