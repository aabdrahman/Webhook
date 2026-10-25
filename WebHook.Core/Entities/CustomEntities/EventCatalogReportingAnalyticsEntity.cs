using System;
using System.Collections.Generic;
using System.Text;

namespace WebHook.Core.Entities.CustomEntities;

/// <summary>
/// Usage statistics for one of the five most-raised active event types. Keyless entity
/// mapped to the <c>vw_GetEventCatalogAnalytics</c> database view.
/// </summary>
public class EventCatalogReportingAnalyticsEntity
{
    /// <summary>The event catalog ID.</summary>
    public Guid Id { get; set; }
    /// <summary>The number of events raised for the type.</summary>
    public int TotalRaisedEvents { get; set; }
    /// <summary>The number of active subscriptions to the event type.</summary>
    public int ActiveSubscriptions { get; set; }
    /// <summary>The normalised event type name.</summary>
    public string NormalizedEventName { get; set; }
    /// <summary>When an event of this type was last raised. Catalogs with no raised events are not returned.</summary>
    public DateTimeOffset? LastRaised { get; set; }
}
