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

public class TrendAnalyticsService : ITrendAnalyticsService
{
    private readonly RepositoryContext _repositoryContext;
    private readonly IAuthenticatedUserDetails _authenticatedUserDetails;

    public TrendAnalyticsService(RepositoryContext repositoryContext, IAuthenticatedUserDetails authenticatedUserDetails)
    {
        _repositoryContext = repositoryContext;
        _authenticatedUserDetails = authenticatedUserDetails;
    }

    private const string _methodName = "MethodName";
    private ILogger _logger = Log.ForContext("ClassName", nameof(TrendAnalyticsService));

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
