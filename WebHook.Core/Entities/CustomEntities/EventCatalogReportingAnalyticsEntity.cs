using System;
using System.Collections.Generic;
using System.Text;

namespace WebHook.Core.Entities.CustomEntities;

public class EventCatalogReportingAnalyticsEntity
{
    public Guid Id { get; set; }
    public int TotalRaisedEvents { get; set; }
    public int ActiveSubscriptions { get; set; }
    public string NormalizedEventName { get; set; }
    public DateTimeOffset? LastRaised { get; set; }
}
