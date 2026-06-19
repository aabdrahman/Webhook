using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookEventCatalog;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Services;
using WebHook.Core.Mapper;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

public sealed class WebhookEventCatalogService : IWebhookEventCatalogService
{
    private readonly RepositoryContext _repositoryContext;
    private ILogger _logger;

    private const string _methodName = "MethodName";
    private const string _className = "ClassName";

    public WebhookEventCatalogService(RepositoryContext repositoryContext)
    {
        _repositoryContext = repositoryContext;
        _logger = Log.ForContext<WebhookEventCatalogService>().ForContext(_className, nameof(WebhookEventCatalogService));
    }

    public async Task<GenericResponse<string>> CreateNewEventCatalogAsync(CreateEventCatalogDto createEventCatalogDto, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(CreateNewEventCatalogAsync));
        try
        {
           _logger.Information("Create new event catalog - {0}", createEventCatalogDto);

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

                return GenericResponse<string>.Success("Operation successful.", "Event Catlog successfully created.", HttpStatusCode.Created);
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

    public async Task<GenericResponse<string>> EventCatalogActivationAsync(Guid EventCatalogId, bool isDeactivate = true, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(EventCatalogActivationAsync));

        try
        {
            _logger.Information("Performing Event Catalog Activation Operation. Is Deactivate - {0}. Event Catalog Id - {1}", isDeactivate, EventCatalogId);

            WebHookEventCatalog? webHookEventCatalogToPerformOperation = await _repositoryContext.WebHookEventCatalogs.FirstOrDefaultAsync(x => x.Id == EventCatalogId, ct);

            if(webHookEventCatalogToPerformOperation is null)
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

                return GenericResponse<string>.Success("Operation Successful", isDeactivate ? "Event Catlog successfully deactivated." : "Event Catalog successfully reactivated.", HttpStatusCode.NoContent);
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

    public async Task<GenericResponse<IReadOnlyList<EventCatalogDto>>> GetAllEventCatalogAsync(CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetAllEventCatalogAsync));
        try
        {
            _logger.Information("Fetching All Event Catalogs");

            IReadOnlyList<EventCatalogDto> allEventCatalogs = await _repositoryContext.WebHookEventCatalogs.Select(EventCatalogMapper.ToDtoExpression()).ToListAsync(ct);

            _logger.Information("Event catalogs fetched successfully - {0}", allEventCatalogs);

            return allEventCatalogs.Any() ? GenericResponse<IReadOnlyList<EventCatalogDto>>.Success(allEventCatalogs, "Event Catlogs successfully fetched.", HttpStatusCode.OK) :
                                            GenericResponse<IReadOnlyList<EventCatalogDto>>.Failure(null, "No event catalog fetched.", HttpStatusCode.NotFound);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred fetching event catalog");
            return GenericResponse<IReadOnlyList<EventCatalogDto>>.Failure(null, "An errror occurred fetching event catalog. please retry later.", HttpStatusCode.InternalServerError, new ErrorDetail() { ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "", ErrorTitle = ex.GetType().Name });
            
        }
    }

    public async Task<GenericResponse<EventCatalogDto>> GetEventCatlogByIdAsync(Guid EventCatlogId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetEventCatlogByIdAsync));

        try
        {
            _logger.Information("Fetching Event Catalog by Id - {0}", EventCatlogId);

            EventCatalogDto? eventCatlog = await _repositoryContext.WebHookEventCatalogs.Select(EventCatalogMapper.ToDtoExpression()).FirstOrDefaultAsync(x => x.Id == EventCatlogId, ct);

            if(eventCatlog is null)
            {
                _logger.Information("Event Catalog with provided id does not exist - {0}", EventCatlogId);
                return GenericResponse<EventCatalogDto>.Failure(null, "Event catalog does not exist.", HttpStatusCode.NotFound);
            }

            _logger.Information("Event catalog with id - {0} fetched successfully - {1}", EventCatlogId, eventCatlog);
            return GenericResponse<EventCatalogDto>.Success(eventCatlog, "Event Catalog fetched successfully.", HttpStatusCode.OK);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred fetching event catalog");
            return GenericResponse<EventCatalogDto>.Failure(null, "An errror occurred fetching event catalog. please retry later.", HttpStatusCode.InternalServerError, new ErrorDetail() { ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "", ErrorTitle = ex.GetType().Name });
        }
    }
}
