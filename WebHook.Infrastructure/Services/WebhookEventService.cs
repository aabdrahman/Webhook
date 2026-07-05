using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using System.Reflection;
using System.Text.Json;
using WebHook.Core.Constants;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookEvent;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Services;
using WebHook.Core.Mapper;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.EventObjectGenerator;

namespace WebHook.Infrastructure.Services;

public sealed class WebhookEventService : IWebhookEventService
{
    private readonly RepositoryContext _repositoryContext;

    public WebhookEventService(RepositoryContext repositoryContext)
    {
        _repositoryContext = repositoryContext;
        _logger = Log.ForContext(_className, nameof(WebhookEventService));
    }

    private ILogger _logger;

    private const string _methodName = "MethodName";
    private const string _className = "ClassName";

    public async Task<GenericResponse<string>> CreateEventAsync(CreateWebhookEventDto createWebhookEvent, CancellationToken ct = default)
    {
        _logger = Log.ForContext(_methodName, nameof(CreateEventAsync));

        try
        {
            _logger.Information("Creating webhook event - {0}", createWebhookEvent);

            //Check that the correlation id is unique
            bool isExistsCorrelationId = await _repositoryContext.WebhookEvents.AsNoTracking().AnyAsync(x => x.CorrelationId == createWebhookEvent.CorrelationId && x.EventType == createWebhookEvent.EventType.ToUpper(), ct);

            if (isExistsCorrelationId)
            {
                _logger.Warning("Correlation Id already exists - {0}", createWebhookEvent.CorrelationId);
                return GenericResponse<string>.Failure("Operation Failed.", "Correlation Id already exists.", HttpStatusCode.Conflict);
            }

            //Check that the raised event is valid
            //This is meant to be optimized further by having a store that holds the valid event types and their corresponding payloads. For now, we will from the database to get all events.
            var eventTypeInCatalog = await _repositoryContext.WebHookEventCatalogs
                                        .AsNoTracking()
                                        .Select(x => new { ActiveStatus = x.IsActive, AvailableFields = x.AvailableFields, x.NormalizedEventName })
                                        .FirstOrDefaultAsync(x => x.NormalizedEventName == createWebhookEvent.EventType.ToUpper(), ct);

            if (eventTypeInCatalog is null)
            {
                _logger.Warning("Invalid event type - {0}", createWebhookEvent.EventType);
                return GenericResponse<string>.Failure("Operation Failed.", "Invalid event type.", HttpStatusCode.BadRequest);
            }

            //Validate that the payload is correct JSON with raised event type object
            try
            {
                var eventTypeProperties = RuntimeEventBuilder.GetPropertyTypes(eventTypeInCatalog.AvailableFields);
                Type raisedEventType = RuntimeEventBuilder.CreateEventType($"{eventTypeInCatalog.NormalizedEventName.ToLower()}Dto", eventTypeProperties);

                object? raisedEventObject = JsonSerializer.Deserialize(createWebhookEvent.PayLoad, raisedEventType, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });

                if (raisedEventObject is null)
                {
                    _logger.Warning("Invalid payload for event type - {0}", createWebhookEvent.EventType);
                    return GenericResponse<string>.Failure("Operation Failed.", "Invalid payload for event type.", HttpStatusCode.BadRequest);
                }

                PropertyInfo[] properties = raisedEventType.GetProperties();
                var anyNullValues = properties.Where(p => p.CanRead && p.CanWrite && p.GetValue(raisedEventObject) is null).Select(x => x.Name).ToList();

                if(anyNullValues.Any())
                {
                    _logger.Warning("Invalid payload for event type - {0}, Missing required fields: {1}", createWebhookEvent.EventType, string.Join(", ", anyNullValues));
                    return GenericResponse<string>.Failure("Operation Failed.", $"Invalid payload for event type. Missing required fields: {string.Join(", ", anyNullValues)}", HttpStatusCode.BadRequest);
                }

            }
            catch (Exception ex)
            {
                _logger.Error(ex, "An error occurred while validating the payload for event type - {0}, {1}", createWebhookEvent.EventType, createWebhookEvent.PayLoad);
                return GenericResponse<string>.Failure("Operation Failed.", "Invalid payload for event type.", HttpStatusCode.BadRequest,
                                                        new ErrorDetail { ErrorMessage = ex.Message, ErrorTitle = ex.GetType().Name, ErrorDescription = ex.InnerException?.Message ?? "" });
            }

            WebhookEvent webhookEvent = WebhookEventMapper.ToEntity(createWebhookEvent);

            await _repositoryContext.WebhookEvents.AddAsync(webhookEvent, ct);

            await _repositoryContext.SaveChangesAsync(ct);

            return GenericResponse<string>.Success(webhookEvent.Id.ToString(), "Webhook event created successfully.", HttpStatusCode.Created);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while creating the webhook event.");
            return GenericResponse<string>.Failure("Operation Failed.", "An error occurred while creating the webhook event.", HttpStatusCode.InternalServerError,
                                                    new ErrorDetail { ErrorMessage = ex.Message, ErrorTitle = ex.GetType().Name, ErrorDescription = ex.InnerException?.Message ?? "" });
        }
    }

    public async Task<GenericResponse<IReadOnlyList<WebhookEventDto>>> GetWebhookEventAsync(Guid correlationId, CancellationToken ct = default)
    {
        _logger = Log.ForContext(_methodName, nameof(GetWebhookEventAsync));

        try
        {
            _logger.Information("Fetching webhook event for correlation id - {0}", correlationId);

            var webhookEvents = await _repositoryContext.WebhookEvents.AsNoTracking().Where(x => x.CorrelationId == correlationId).Select(WebhookEventMapper.ToDtoExpression()).ToListAsync(ct);

            if(!webhookEvents.Any())
            {
                _logger.Warning("Webhook event not found for correlation id - {0}", correlationId);
                return GenericResponse<IReadOnlyList<WebhookEventDto>>.Failure(null, "Webhook event not found.", HttpStatusCode.NotFound);
            }

            _logger.Information("Successfully fetched webhook event for correlation id - {0}, {1}", correlationId, webhookEvents);
            return GenericResponse<IReadOnlyList<WebhookEventDto>>.Success(webhookEvents, "Webhook event fetched successfully.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error ocurred while getting webhook event details.");
            return GenericResponse<IReadOnlyList<WebhookEventDto>>.Failure(null, "An error occurred while fetching the webhook event.", HttpStatusCode.InternalServerError, 
                                                            new ErrorDetail { ErrorMessage = ex.Message, ErrorTitle = ex.GetType().Name, ErrorDescription = ex.InnerException?.Message ?? "" });
            
        }
    }

    public async Task<GenericResponse<IReadOnlyList<WebhookEventDto>>> GetWebhookEventsAsync(GetWebhookEventParameters parameters, CancellationToken ct = default)
    {
        _logger = Log.ForContext(_methodName, nameof(GetWebhookEventsAsync));

        try
        {
            _logger.Information("Fetching webhook events. Parameters: {0}", parameters);

            var query = _repositoryContext.WebhookEvents.AsNoTracking().Where(x => x.CreatedAt >= parameters.CreatedAtFrom && x.CreatedAt <= parameters.CreatedAtTo).AsQueryable();

            if (!string.IsNullOrWhiteSpace(parameters.Source))
            {
                query = query.Where(x => x.Source == parameters.Source);
            }

            if(!string.IsNullOrWhiteSpace(parameters.EventType))
            {
                query = query.Where(x => x.EventType == parameters.EventType.ToUpper());
            }

            if(!string.IsNullOrWhiteSpace(parameters.Status) && Enum.TryParse<WebHookEventStatus>(parameters.Status, true, out var enumStatus))
            {
                query = query.Where(x => x.Status == enumStatus);
            }

            if(parameters.CorrelationId.HasValue)
            {
                query = query.Where(x => x.CorrelationId == parameters.CorrelationId.Value);
            }

            IReadOnlyList<WebhookEventDto> webhookEvents = await query.Select(WebhookEventMapper.ToDtoExpression()).ToListAsync(ct);

            _logger.Information("Successfully fetched webhook events. Count: {0}, {1}", webhookEvents.Count, webhookEvents);

            return GenericResponse<IReadOnlyList<WebhookEventDto>>.Success(webhookEvents, "Webhook events fetched successfully.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error ocurred while getting webhook events.");
            return GenericResponse<IReadOnlyList<WebhookEventDto>>.Failure(null, "An error occurred while fetching the webhook events.", HttpStatusCode.InternalServerError,
                                                            new ErrorDetail { ErrorMessage = ex.Message, ErrorTitle = ex.GetType().Name, ErrorDescription = ex.InnerException?.Message ?? "" });

        }
    }
}
