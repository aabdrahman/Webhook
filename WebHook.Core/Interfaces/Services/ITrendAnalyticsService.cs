using System;
using System.Collections.Generic;
using System.Text;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.TrendAnalytics;

namespace WebHook.Core.Interfaces.Services;


/// <summary>
/// Provides data for the trend analytics page. Work in progress.
/// </summary>
public interface ITrendAnalyticsService
{
    /// <summary>
    /// Gets the subscriptions and event types the caller can filter trend analytics by.
    /// Admins see all of them; other users see their own.
    /// </summary>
    /// <remarks>Not yet implemented: the current implementation throws <see cref="NotImplementedException"/>.</remarks>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A <see cref="GenericResponse{T}"/> of <see cref="TrendAnalyticsFilterDetails"/>.</returns>
    Task<GenericResponse<TrendAnalyticsFilterDetails>> GetTrendAnalyticsDetails(CancellationToken ct = default);
}
