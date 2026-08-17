using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookEventCatalog;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Services;
using WebHook.Core.Mapper;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.EventObjectGenerator;

namespace WebHook.Infrastructure.Services;

/// <summary>
/// Service responsible for managing webhook event catalogs.
/// Implements <see cref="IWebhookEventCatalogService"/> and provides operations for
/// creating, retrieving, updating, activating/deactivating, and deleting webhook event catalogs.
/// The service utilizes the repository layer for persistence and the logger for
/// recording application events and errors.
/// </summary>
public sealed class WebhookEventCatalogService : IWebhookEventCatalogService
{
    private readonly RepositoryContext _repositoryContext;
    private ILogger _logger;

    private const string _methodName = "MethodName";
    private const string _className = "ClassName";

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookEventCatalogService"/> class.
    /// </summary>
    /// <param name="repositoryContext">
    /// The repository context used to perform data access operations.
    /// </param>
    /// <param name="logger">
    /// The logger used for recording application events and errors.
    /// </param>
    public WebhookEventCatalogService(RepositoryContext repositoryContext)
    {
        _repositoryContext = repositoryContext;
        _logger = Log.ForContext<WebhookEventCatalogService>().ForContext(_className, nameof(WebhookEventCatalogService));
    }

    /// <summary>
    /// Creates a new webhook event type that subscribers can subscribe to.
    /// </summary>
    /// <param name="createEventCatalogDto">
    /// The details of the webhook event type.
    /// </param>
    /// <param name="ct">
    /// The cancellation token.
    /// </param>
    /// <returns>
    /// A generic response containing the outcome of the operation.
    /// </returns>
    public async Task<GenericResponse<string>> CreateNewEventCatalogAsync(CreateEventCatalogDto createEventCatalogDto, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(CreateNewEventCatalogAsync));
        try
        {
            _logger.Information("Create new event catalog - {0}", createEventCatalogDto);

            //Validate the available fields dictionary to ensure that the values are of the correct type (string, int, bool, guid, datetime, float, decimal or double). If any value is not of the correct type, return a failure response.
            if (!createEventCatalogDto.AvailableFields.All(kvp => kvp.Value.ToLower() == "string" || kvp.Value == "int" ||
                                                            kvp.Value == "bool" || kvp.Value == "guid" ||
                                                            kvp.Value == "datetime" || kvp.Value == "double" ||
                                                            kvp.Value == "decimal" || kvp.Value == "float")
                )
            {
                _logger.Information("Invalid field types in available fields - {0}", createEventCatalogDto.AvailableFields);
                return GenericResponse<string>.Failure(null, "Invalid field types in available fields.", HttpStatusCode.BadRequest);
            }

            //Validate that a class can e created with the available fields dictionary. If not, return a failure response.
            try
            {
                Dictionary<string, Type> eventPropertiesType = RuntimeEventBuilder.GetPropertyTypes(createEventCatalogDto.AvailableFields);
                Type eventType = RuntimeEventBuilder.CreateEventType($"{createEventCatalogDto.EventCatalogName.ToLower()}Dto", eventPropertiesType);
                _logger.Information("Event type created successfully - {0}", eventType);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "An error occurred while creating the event type.");
                return GenericResponse<string>.Failure(null, "An error occurred while creating the event type with provided properties.", HttpStatusCode.BadRequest);
            }

            bool isNameExists = await _repositoryContext.WebHookEventCatalogs.AnyAsync(x => x.NormalizedEventName == createEventCatalogDto.EventCatalogName.ToUpper(), ct);

            if (isNameExists)
            {
                _logger.Information("Event Catalog with name already exists - {0}", createEventCatalogDto.EventCatalogName);
                return GenericResponse<string>.Failure(null, "Event Catalog already exists.", HttpStatusCode.Conflict);
            }

            WebHookEventCatalog webHookEventCatalogToInsert = createEventCatalogDto.ToEntity();

            await _repositoryContext.WebHookEventCatalogs.AddAsync(webHookEventCatalogToInsert);

            try
            {
                await _repositoryContext.SaveChangesAsync(ct);

                _logger.Information("Webhook Event Catalog successfully created - {0}", webHookEventCatalogToInsert.ToDto());

                return GenericResponse<string>.Success("Operation successful.", "Event Catalog successfully created.", HttpStatusCode.Created);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error occurred while inserting new event catalog record into database - {0}", createEventCatalogDto);
                return GenericResponse<string>.Failure("Operation Failed", "We could not perform operation. Please retry later.", HttpStatusCode.InternalServerError, new ErrorDetail() { ErrorDescription = ex.InnerException?.Message ?? "", ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error occurred while performing new event catalog creation operation - {0}", createEventCatalogDto);
            return GenericResponse<string>.Failure("Operation Failed", "We could not perform operation. Please retry later.", HttpStatusCode.InternalServerError, new ErrorDetail() { ErrorDescription = ex.InnerException?.Message ?? "", ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message });
        }
    }
    /// <summary>
    /// Activates or deactivates a webhook event type, controlling whether subscribers can register for the event.
    /// </summary>
    /// <param name="eventCatalogId">
    /// The unique identifier of the webhook event type.
    /// </param>
    /// <param name="isDeactivate">
    /// Indicates whether the event type should be deactivated.
    /// </param>
    /// <param name="ct">
    /// The cancellation token.
    /// </param>
    /// <returns>
    /// A generic response containing the outcome of the operation.
    /// </returns>
    public async Task<GenericResponse<string>> EventCatalogActivationAsync(Guid EventCatalogId, bool isDeactivate = true, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(EventCatalogActivationAsync));

        try
        {
            _logger.Information("Performing Event Catalog Activation Operation. Is Deactivate - {0}. Event Catalog Id - {1}", isDeactivate, EventCatalogId);

            WebHookEventCatalog? webHookEventCatalogToPerformOperation = await _repositoryContext.WebHookEventCatalogs.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == EventCatalogId, ct);

            if (webHookEventCatalogToPerformOperation is null)
            {
                _logger.Information("Web Event Catalog with provided Id does not exist - {0}", EventCatalogId);
                return GenericResponse<string>.Failure("Operation Failed", "Event Catalog with provided id does not exist.", HttpStatusCode.NotFound);
            }

            if (isDeactivate)
            {
                _logger.Information("Deactivate Event Catalog for subscribers - {0}", EventCatalogId);
                webHookEventCatalogToPerformOperation.IsActive = false;
            }
            else
            {
                _logger.Information("Activate Event Catalog for subscribers - {0}", EventCatalogId);
                webHookEventCatalogToPerformOperation.IsActive = true;
            }

            try
            {
                await _repositoryContext.SaveChangesAsync(ct);

                return GenericResponse<string>.Success("Operation Successful", isDeactivate ? "Event Catalog successfully deactivated." : "Event Catalog successfully reactivated.", HttpStatusCode.NoContent);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, isDeactivate ? "Deactivation operation for event category could not be completed when saving to database - {0}" : "Activation operation for event category could not be completed when saving to database - {0}", EventCatalogId);
                return GenericResponse<string>.Failure("Operation failed.", isDeactivate ? "Event Catalog deactivation could not be completed. please retry later." : "Event Catalog reactivation could not be completed. please retry later.", HttpStatusCode.InternalServerError);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, isDeactivate ? "Deactivation operation for event category could not be completed - {0}" : "Activation operation for event category could not be completed - {0}", EventCatalogId);
            return GenericResponse<string>.Failure("Operation failed.", isDeactivate ? "Event Catalog deactivation could not be completed. please retry later." : "Event Catalog reactivation could not be completed. please retry later.", HttpStatusCode.InternalServerError);
        }
    }
    /// <summary>
    /// Retrieves all webhook event catalogs.
    /// </summary>
    /// <param name="ct">
    /// The cancellation token.
    /// </param>
    /// <returns>
    /// A generic response containing the collection of webhook event catalogs.
    /// </returns>
    public async Task<GenericResponse<IReadOnlyList<EventCatalogDto>>> GetAllEventCatalogAsync(CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetAllEventCatalogAsync));
        try
        {
            _logger.Information("Fetching All Event Catalogs");

            IReadOnlyList<EventCatalogDto> allEventCatalogs = await _repositoryContext.WebHookEventCatalogs.Select(EventCatalogMapper.ToDtoExpression()).ToListAsync(ct);

            _logger.Information("Event catalogs fetched successfully - {0}", allEventCatalogs);

            return allEventCatalogs.Any() ? GenericResponse<IReadOnlyList<EventCatalogDto>>.Success(allEventCatalogs, "Event Catalogs successfully fetched.", HttpStatusCode.OK) :
                                            GenericResponse<IReadOnlyList<EventCatalogDto>>.Failure(null, "No event catalog fetched.", HttpStatusCode.NotFound);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred fetching event catalog");
            return GenericResponse<IReadOnlyList<EventCatalogDto>>.Failure(null, "An errror occurred fetching event catalog. please retry later.", HttpStatusCode.InternalServerError, new ErrorDetail() { ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "", ErrorTitle = ex.GetType().Name });

        }
    }
    /// <summary>
    /// Retrieves a webhook event catalog by its unique identifier.
    /// </summary>
    /// <param name="eventCatalogId">
    /// The unique identifier of the webhook event catalog.
    /// </param>
    /// <param name="ct">
    /// The cancellation token.
    /// </param>
    /// <returns>
    /// A generic response containing the requested webhook event catalog.
    /// </returns>
    public async Task<GenericResponse<EventCatalogDto>> GetEventCatalogByIdAsync(Guid EventCatalogId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetEventCatalogByIdAsync));

        try
        {
            _logger.Information("Fetching Event Catalog by Id - {0}", EventCatalogId);

            EventCatalogDto? eventCatlog = await _repositoryContext.WebHookEventCatalogs.Select(EventCatalogMapper.ToDtoExpression()).FirstOrDefaultAsync(x => x.Id == EventCatalogId, ct);

            if (eventCatlog is null)
            {
                _logger.Information("Event Catalog with provided id does not exist - {0}", EventCatalogId);
                return GenericResponse<EventCatalogDto>.Failure(null, "Event catalog does not exist.", HttpStatusCode.NotFound);
            }

            _logger.Information("Event catalog with id - {0} fetched successfully - {1}", EventCatalogId, eventCatlog);
            return GenericResponse<EventCatalogDto>.Success(eventCatlog, "Event Catalog fetched successfully.", HttpStatusCode.OK);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred fetching event catalog");
            return GenericResponse<EventCatalogDto>.Failure(null, "An errror occurred fetching event catalog. please retry later.", HttpStatusCode.InternalServerError, new ErrorDetail() { ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "", ErrorTitle = ex.GetType().Name });
        }
    }
}
