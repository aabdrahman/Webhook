using System;
using System.Collections.Generic;
using System.Text;

namespace WebHook.Core.RequestParameter;

public class TrendAnalyticsRequestParameterDto
{
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Now.AddDays(-7));
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    public Guid? SubscriptionId { get; set; }
    public Guid? EventCatalogId { get; set; }
    public string? Status { get; set; }
}
