using System;
using System.Collections.Generic;
using System.Text;

namespace WebHook.Core.Entities.CustomEntities;

/// <summary>
/// One of the five most recently raised events with its delivery count, for the admin dashboard.
/// Keyless entity mapped to the <c>vw_getmostrecentevents</c> database view.
/// </summary>
/// <remarks>
/// Only events that have at least one delivery are included.
/// </remarks>
public class TopFiveEventAnalyticsEntity
{
    /// <summary>The event type.</summary>
    public string EventType { get; set; }
    /// <summary>The service that raised the event.</summary>
    public string Source { get; set; }
    /// <summary>The number of deliveries created for the event.</summary>
    public int DeliveryCount { get; set; }
    /// <summary>The event's correlation ID.</summary>
    public Guid CorrelationId { get; set; }
    /// <summary>When the event was raised.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
