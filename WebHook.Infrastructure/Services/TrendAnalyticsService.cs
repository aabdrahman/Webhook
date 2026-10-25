using Serilog;
using System;
using System.Collections.Generic;
using System.Text;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.TrendAnalytics;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

/// <summary>
/// <see cref="ITrendAnalyticsService"/> implementation. Work in progress.
/// </summary>
public class TrendAnalyticsService : ITrendAnalyticsService
{
    private readonly RepositoryContext _repositoryContext;
    private readonly IAuthenticatedUserDetails _authenticatedUserDetails;

    /// <summary>
    /// Initialises a new instance of the <see cref="TrendAnalyticsService"/> class.
    /// </summary>
    /// <param name="repositoryContext">The database context.</param>
    /// <param name="authenticatedUserDetails">The current request's caller, used to scope results.</param>
    public TrendAnalyticsService(RepositoryContext repositoryContext, IAuthenticatedUserDetails authenticatedUserDetails)
    {
        _repositoryContext = repositoryContext;
        _authenticatedUserDetails = authenticatedUserDetails;
    }

    private const string _methodName = "MethodName";
    private ILogger _logger = Log.ForContext("ClassName", nameof(TrendAnalyticsService));

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Always thrown; the method is not finished.</exception>
    public Task<GenericResponse<TrendAnalyticsFilterDetails>> GetTrendAnalyticsDetails(CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetTrendAnalyticsDetails));
        try
        {
            _logger.Information("Fetching user trend analytics filter details....");

            if(_authenticatedUserDetails.assignedRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            {
                var subscriptionResult = _repositoryContext.WebhookEventSubscriptions;
                var eventsResult = _repositoryContext.WebhookEventSubscriptions;
            }
            else
            {
                if(!Guid.TryParse(_authenticatedUserDetails.userId, out Guid authUserId))
                {

                }

            }

        }
        catch (Exception ex)
        {

            throw;
        }
        throw new NotImplementedException();
    }
}
