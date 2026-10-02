using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.Entities.CustomEntities;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

public class ReportingAnalyticsService : IReportingAnalyticsService
{
    private readonly RepositoryContext _repositoryContext;

    public ReportingAnalyticsService(RepositoryContext repositoryContext)
    {
        _repositoryContext = repositoryContext;
    }

    private const string _methodName = "MethodName";
    private Serilog.ILogger _logger = Log.ForContext("ClassName", nameof(ReportingAnalyticsService));

    public async Task<GenericResponse<IReadOnlyList<TopFiveEventAnalyticsEntity>>> GetMostRecentEventsAsync(CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetMostRecentEventsAsync));
        try
        {
            _logger.Information("Fetching top five most recent events...");

            List<TopFiveEventAnalyticsEntity> topFiveEvents = await _repositoryContext.TopFiveEventAnalyticsEntities.ToListAsync(ct);

            _logger.Information("Top five events fetched successfully - {0}", topFiveEvents);

            return GenericResponse<IReadOnlyList<TopFiveEventAnalyticsEntity>>.Success(topFiveEvents, "Most recent events fetched successfully.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while fetching top five most recent events...");
            return GenericResponse<IReadOnlyList<TopFiveEventAnalyticsEntity>>.Failure(null, "An error occurred while fetching top five most recent events.", HttpStatusCode.InternalServerError, new ErrorDetail { ErrorMessage = ex.Message });
        }

    }

    public async Task<GenericResponse<IReadOnlyList<EventCatalogReportingAnalyticsEntity>>> GetTopActiveEventCatalogsAsync(CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetTopActiveEventCatalogsAsync));
        try
        {
            _logger.Information("Getting top five active event catalogs...");

            List<EventCatalogReportingAnalyticsEntity> items = await _repositoryContext.EventCatalogReportingAnalyticsEntities.ToListAsync(ct);

            _logger.Information("Top five event catalogs fetched successfully - {0}", items);

            return GenericResponse<IReadOnlyList<EventCatalogReportingAnalyticsEntity>>.Success(items, "Top active event catalogs fetched successfully.", HttpStatusCode.OK);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while fetching event catalogs analytics.");
            return GenericResponse<IReadOnlyList<EventCatalogReportingAnalyticsEntity>>.Failure(null, "An error occurred while fetching event catalogs analytics.", HttpStatusCode.InternalServerError, new ErrorDetail { ErrorMessage = ex.Message });
        }
    }
}
