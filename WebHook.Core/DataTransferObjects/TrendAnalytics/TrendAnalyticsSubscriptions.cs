using System;
using System.Collections.Generic;
using System.Text;

namespace WebHook.Core.DataTransferObjects.TrendAnalytics;

public record TrendAnalyticsSubscriptions
(Guid Id, string SubscriptionName);

public record TrendAnalyticsEventCatalogs
(Guid Id, string EventName);

public class TrendAnalyticsFilterDetails
{
    public List<TrendAnalyticsSubscriptions> Subscriptions { get; set; } = [];
    public List<TrendAnalyticsEventCatalogs> EventCatalogs { get; set; } = [];
}
