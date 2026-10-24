using System;
using System.Collections.Generic;
using System.Text;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.TrendAnalytics;

namespace WebHook.Core.Interfaces.Services;


public interface ITrendAnalyticsService
{
    Task<GenericResponse<TrendAnalyticsFilterDetails>> GetTrendAnalyticsDetails(CancellationToken ct = default);
}
