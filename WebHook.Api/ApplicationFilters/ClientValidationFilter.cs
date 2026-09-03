using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Api.ApplicationFilters;

/// <summary>
/// An authorization filter that authenticates incoming requests from onboarded
/// internal service clients attempting to publish webhook events.
/// </summary>
/// <remarks>
/// This filter is applied to the <c>POST /api/webhookevent</c> endpoint and
/// validates the <c>X-Client-Id</c> and <c>X-Client-Key</c> headers present
/// on every publish request from an internal service.
///
/// The validation flow is:
/// <list type="number">
///   <item><description>Both <c>X-Client-Id</c> and <c>X-Client-Key</c> headers must be present and non-empty — returns <c>401</c> if either is missing.</description></item>
///   <item><description>The <c>ClientId</c> is looked up in the <c>WebhookServiceClients</c> table — returns <c>401</c> if no matching active client is found.</description></item>
///   <item><description>The raw <c>ClientKey</c> is validated against the stored hash using <see cref="IApplicationHasher"/> — returns <c>401</c> if the hash does not match.</description></item>
/// </list>
///
/// All failure responses return <c>401 Unauthorized</c> with a generic
/// <c>"Unauthorized Access"</c> message — the specific failure reason is never
/// disclosed to the caller to prevent credential enumeration.
///
/// On success the request proceeds to the controller action where event type
/// authorisation against the client's assigned catalog is performed by the
/// service layer.
/// </remarks>
/// <param name="repositoryContext">
/// The EF Core database context used to look up the service client record
/// by <c>ClientId</c>.
/// </param>
/// <param name="applicationHasher">
/// The hasher used to validate the raw <c>ClientKey</c> against the stored hash.
/// The raw key is never stored — only its hash is persisted at onboarding time.
/// </param>
public class ClientValidationFilter(RepositoryContext repositoryContext, IApplicationHasher applicationHasher) : IAsyncAuthorizationFilter
{
    private Serilog.ILogger _logger = Log.ForContext("ClassName", nameof(ClientValidationFilter));

    /// <summary>
    /// Intercepts the request and validates the service client credentials
    /// supplied in the <c>X-Client-Id</c> and <c>X-Client-Key</c> headers
    /// before the controller action is invoked.
    /// </summary>
    /// <param name="context">
    /// The authorization filter context providing access to the HTTP request
    /// headers and allowing the filter to short-circuit the pipeline by setting
    /// <see cref="AuthorizationFilterContext.Result"/>.
    /// </param>
    public virtual async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        _logger = _logger.ForContext("MethodName", nameof(OnAuthorizationAsync));
        string clientKey = string.Empty;
        string clientId = string.Empty;
        if(context.HttpContext.Request.Headers.TryGetValue("X-Client-Key", out var clientKeyValue))
        {
            clientKey = clientKeyValue.First()!.ToString();
        }

        if(context.HttpContext.Request.Headers.TryGetValue("X-Client-Id", out var clientIdValue))
        {
            clientId = clientIdValue.First()!.ToString();
        }

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientKey))
        {
            context.Result = new UnauthorizedObjectResult(GenericResponse<string>.Failure(null, "Unauthorized Access", HttpStatusCode.Unauthorized));
            return;
        }

        WebhookServiceClient? clientDetails = await repositoryContext.WebhookServiceClients.FirstOrDefaultAsync(x => x.ClientId == clientId.ToLower());
        if(clientDetails is null)
        {
            context.Result = new UnauthorizedObjectResult(GenericResponse<string>.Failure(null, "Unauthorized Access", HttpStatusCode.Unauthorized));
            return;
        }

        bool isValidKey = await applicationHasher.ValidateHashedSecret(clientKey, clientDetails.ClientKey);

        if (!isValidKey)
        {
            _logger.Warning("Service Client Id: {0} provided the wrong key.", clientId, clientKey);
            context.Result = new UnauthorizedObjectResult(GenericResponse<string>.Failure(null, "Unauthorized Access", HttpStatusCode.Unauthorized));
            return;
        }

        _logger.Information("Service Client: {0} authenticated successfully - {0}", clientId);

    }
}
