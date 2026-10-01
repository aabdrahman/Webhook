using System;
using System.Collections.Generic;
using System.Text;

namespace WebHook.Core.Entities.CustomEntities;

public class TopFiveEventAnalyticsEntity
{
    public string EventType { get; set; }
    public string Source { get; set; }
    public int DeliveryCount { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
