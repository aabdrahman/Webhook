using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using System.Reflection;
using System.Text.Json;
using WebHook.Core.Constants;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookEvent;
using WebHook.Core.Entities;
using WebHook.Core.EventContracts.Publishers;
using WebHook.Core.Interfaces.Services;
using WebHook.Core.Mapper;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.EventObjectGenerator;

namespace WebHook.Infrastructure.Services;

/// <summary>
/// Provides operations for creating and querying webhook events — the runtime
/// occurrences of event types defined in the <c>EventCatalog</c>.
/// </summary>
/// <remarks>
/// <para>
/// This service is responsible for the full lifecycle of a raised event up to
/// the point of persistence. It does not handle delivery, retry, or dead-letter
/// logic — those concerns belong to the delivery pipeline (
/// <c>DeliveryService</c>, <c>RetryProcessor</c>, <c>WebhookDispatcherService</c>).
/// </para>
/// <para>
/// Payload validation is performed dynamically at runtime using
/// <see cref="RuntimeEventBuilder"/>. The catalog's <c>AvailableFields</c>
/// dictionary is used to construct a CLR type on the fly, and the submitted
/// JSON payload is deserialised against that type. This ensures the payload
/// structure is always consistent with what the catalog declares — without
/// requiring a compile-time model for every possible event type.
/// </para>
/// </remarks>
public sealed class WebhookEventService : IWebhookEventService
{
    private readonly RepositoryContext _repositoryContext;
    private readonly IApplicationPublisher _applicationPublisher;

    /// <summary>
    /// Initializes a new instance of <see cref="WebhookEventService"/>.
    /// </summary>
    /// <param name="repositoryContext">
    /// The EF Core database context used for all persistence and query operations.
    /// </param>
    /// <paramref name="applicationPublisher"/>
    /// The application configured publisher to publish to channels designated for eacj possible operation.
    /// </param>
    public WebhookEventService(RepositoryContext repositoryContext, IApplicationPublisher applicationPublisher)
    {
        _repositoryContext = repositoryContext;
        _applicationPublisher = applicationPublisher;
        _logger = Log.ForContext(_className, nameof(WebhookEventService));
    }

    private ILogger _logger;

    private const string _methodName = "MethodName";
    private const string _className = "ClassName";

    /// <summary>
    /// Creates and persists a new webhook event after performing three
    /// sequential validation steps.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Step 1 — Correlation ID uniqueness check:</strong><br/>
    /// Queries <c>WebhookEvents</c> for an existing record matching both the
    /// supplied <c>CorrelationId</c> and the normalised <c>EventType</c>.
    /// If a match is found the operation is rejected with
    /// <see cref="HttpStatusCode.Conflict"/>. This prevents the same business
    /// transaction from raising the same event type more than once.
    /// </para>
    /// <para>
    /// <strong>Step 2 — Event type catalog validation:</strong><br/>
    /// Looks up the event type in <c>WebHookEventCatalogs</c> using a
    /// normalised (uppercase) comparison against <c>NormalizedEventName</c>.
    /// If the event type is not found in the catalog the operation is rejected
    /// with <see cref="HttpStatusCode.BadRequest"/>. Only the fields needed for
    /// validation (<c>IsActive</c>, <c>AvailableFields</c>,
    /// <c>NormalizedEventName</c>) are projected to avoid loading the full
    /// entity.
    /// </para>
    /// <para>
    /// <strong>Step 3 — Payload structure validation:</strong><br/>
    /// Uses <see cref="RuntimeEventBuilder.GetPropertyTypes"/> and
    /// <see cref="RuntimeEventBuilder.CreateEventType"/> to dynamically construct
    /// the expected CLR type from the catalog's <c>AvailableFields</c>. The
    /// submitted JSON payload is then deserialised against this type using
    /// <see cref="JsonSerializer"/> with <c>PropertyNameCaseInsensitive = true</c>.
    /// After deserialisation, all writable properties are inspected via
    /// reflection — any that are <see langword="null"/> are treated as missing
    /// required fields and reported individually in the failure response message.
    /// This inner block has its own <c>try/catch</c> so payload validation
    /// errors return <see cref="HttpStatusCode.BadRequest"/> rather than
    /// <see cref="HttpStatusCode.InternalServerError"/>.
    /// </para>
    /// <para>
    /// If all three steps pass, the event is mapped to a
    /// <see cref="WebhookEvent"/> entity via <see cref="WebhookEventMapper.ToEntity"/>
    /// and persisted in a single <c>SaveChangesAsync</c> call. The new event's
    /// ID is returned as the response data.
    /// </para>
    /// </remarks>
    /// <param name="createWebhookEvent">
    /// The details of the event to raise, including the event type, JSON
    /// payload, source service identifier, and optional correlation ID.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the asynchronous
    /// operation before it completes.
    /// </param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/> where the
    /// data field contains the new event's ID. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="HttpStatusCode.Created"/> — event persisted successfully.</description></item>
    ///   <item><description><see cref="HttpStatusCode.Conflict"/> — duplicate CorrelationId + EventType combination.</description></item>
    ///   <item><description><see cref="HttpStatusCode.BadRequest"/> — unknown event type, null/malformed payload, or missing required payload fields.</description></item>
    ///   <item><description><see cref="HttpStatusCode.InternalServerError"/> — unexpected error; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
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

            await _applicationPublisher.QueueEventRaised(new Core.EventContracts.Events.EventRaised(webhookEvent.Id));

            return GenericResponse<string>.Success(webhookEvent.Id.ToString(), "Webhook event created successfully.", HttpStatusCode.Created);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while creating the webhook event.");
            return GenericResponse<string>.Failure("Operation Failed.", "An error occurred while creating the webhook event.", HttpStatusCode.InternalServerError,
                                                    new ErrorDetail { ErrorMessage = ex.Message, ErrorTitle = ex.GetType().Name, ErrorDescription = ex.InnerException?.Message ?? "" });
        }
    }

    /// <summary>
    /// Retrieves all webhook events that share the given correlation ID.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A correlation ID groups all events raised by a single originating
    /// business transaction. For example, a customer onboarding flow may
    /// raise both <c>CustomerCreated</c> and <c>AccountApproved</c>, both
    /// carrying the same correlation ID. This method returns all of them.
    /// </para>
    /// <para>
    /// The query uses <c>AsNoTracking</c> since the results are read-only
    /// DTOs projected server-side via
    /// <see cref="WebhookEventMapper.ToDtoExpression"/>. No entity graph is
    /// loaded into memory.
    /// </para>
    /// <para>
    /// Unlike <see cref="GetWebhookEventsAsync"/>, this method returns
    /// <see cref="HttpStatusCode.NotFound"/> when no events are found —
    /// because a caller querying by a specific correlation ID expects a result
    /// to exist. An empty result is an error condition here, not a valid
    /// filtered outcome.
    /// </para>
    /// </remarks>
    /// <param name="correlationId">
    /// The correlation ID of the originating business transaction whose
    /// events should be retrieved.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the asynchronous
    /// operation before it completes.
    /// </param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of
    /// <see cref="IReadOnlyList{WebhookEventDto}"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="HttpStatusCode.OK"/> — one or more events found.</description></item>
    ///   <item><description><see cref="HttpStatusCode.NotFound"/> — no events exist for the provided correlation ID.</description></item>
    ///   <item><description><see cref="HttpStatusCode.InternalServerError"/> — unexpected error; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
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
    /// <summary>
    /// Retrieves a filtered list of webhook events using the provided query
    /// parameters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The date range filter (<see cref="GetWebhookEventParameters.CreatedAtFrom"/>
    /// to <see cref="GetWebhookEventParameters.CreatedAtTo"/>) is the mandatory
    /// base filter applied to every query. Both boundary values are inclusive
    /// (<c>&gt;=</c> and <c>&lt;=</c>). All remaining parameters are optional
    /// and applied conditionally:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <see cref="GetWebhookEventParameters.Source"/> — exact match on the
    ///       originating service name. Applied only when the value is not null
    ///       or whitespace.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="GetWebhookEventParameters.EventType"/> — normalised to
    ///       uppercase before comparison against <c>WebhookEvent.EventType</c>.
    ///       Applied only when the value is not null or whitespace.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="GetWebhookEventParameters.Status"/> — parsed
    ///       case-insensitively using <see cref="Enum.TryParse{TEnum}"/>.
    ///       If the string does not map to a known <c>WebHookEventStatus</c>
    ///       value the filter is silently skipped — no error is returned.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="GetWebhookEventParameters.CorrelationId"/> — exact GUID
    ///       match. Applied only when the value is not <see langword="null"/>.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// All filters are composed into a single deferred LINQ query that is
    /// translated to SQL and executed in one round-trip. The projection to
    /// <see cref="WebhookEventDto"/> is performed server-side via
    /// <see cref="WebhookEventMapper.ToDtoExpression"/> to avoid loading full
    /// entity graphs. <c>AsNoTracking</c> is applied since results are
    /// read-only.
    /// </para>
    /// <para>
    /// This method always returns <see cref="HttpStatusCode.OK"/>, even when
    /// the filtered result set is empty. An empty list is a valid query
    /// outcome, not an error condition.
    /// </para>
    /// </remarks>
    /// <param name="parameters">
    /// The query parameters. <c>CreatedAtFrom</c> and <c>CreatedAtTo</c> are
    /// mandatory; all other fields are optional filters.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the asynchronous
    /// operation before it completes.
    /// </param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of
    /// <see cref="IReadOnlyList{WebhookEventDto}"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="HttpStatusCode.OK"/> — query executed successfully; data may be an empty list.</description></item>
    ///   <item><description><see cref="HttpStatusCode.InternalServerError"/> — unexpected error; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
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
