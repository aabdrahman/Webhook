using Serilog;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookEventCatalog;
using WebHook.Core.Interfaces.Services;
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
        try
        {
            _logger.ForContext(_methodName, nameof(CreateNewEventCatalogAsync)).Information("Create new event catalog - {0}", createEventCatalogDto);

            //bool isNameExists = await _repositoryContext.web
        }
        catch (Exception ex)
        {

            throw;
        }

        throw new NotImplementedException();
    }

    public Task<GenericResponse<string>> EventCatalogActivationAsync(Guid EventCatalogId, bool isDeactivate = true, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public GenericResponse<IReadOnlyList<EventCatalogDto>> GetAllEventCatalogAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<GenericResponse<EventCatalogDto>> GetEventCatlogByIdAsync(Guid EventCatlogId, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}
