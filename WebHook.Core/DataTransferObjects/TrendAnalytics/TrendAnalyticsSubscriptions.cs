using System;
using System.Collections.Generic;
using System.Text;

namespace WebHook.Core.DataTransferObjects.TrendAnalytics;

/// <summary>
/// A subscription option for the trend analytics filter.
/// </summary>
/// <param name="Id">The subscription ID.</param>
/// <param name="SubscriptionName">The subscription name to display.</param>
public record TrendAnalyticsSubscriptions
(Guid Id, string SubscriptionName);

/// <summary>
/// An event catalog option for the trend analytics filter.
/// </summary>
/// <param name="Id">The event catalog ID.</param>
/// <param name="EventName">The event type name to display.</param>
public record TrendAnalyticsEventCatalogs
(Guid Id, string EventName);

/// <summary>
/// The options available to the trend analytics filter bar.
/// </summary>
public class TrendAnalyticsFilterDetails
{
    /// <summary>The subscriptions that can be filtered on.</summary>
    public List<TrendAnalyticsSubscriptions> Subscriptions { get; set; } = [];
    /// <summary>The event types that can be filtered on.</summary>
    public List<TrendAnalyticsEventCatalogs> EventCatalogs { get; set; } = [];
}
