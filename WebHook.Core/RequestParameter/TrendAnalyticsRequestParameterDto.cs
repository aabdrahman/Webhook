using System;
using System.Collections.Generic;
using System.Text;

namespace WebHook.Core.RequestParameter;

/// <summary>
/// Filter parameters for the trend analytics page. Not yet used by any endpoint.
/// </summary>
public class TrendAnalyticsRequestParameterDto
{
    /// <summary>The start of the date range. Defaults to seven days ago (server local time).</summary>
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Now.AddDays(-7));
    /// <summary>The end of the date range. Defaults to today (server local time).</summary>
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    /// <summary>Optionally limits results to one subscription.</summary>
    public Guid? SubscriptionId { get; set; }
    /// <summary>Optionally limits results to one event type.</summary>
    public Guid? EventCatalogId { get; set; }
    /// <summary>Optionally limits results to one delivery status.</summary>
    public string? Status { get; set; }
}
