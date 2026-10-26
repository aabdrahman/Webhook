using System;
using System.Collections.Generic;
using System.Text;

namespace WebHook.Core.DataTransferObjects.ReportingAnalytics;

/// <summary>
/// A recent delivery for one of the current user's subscriptions, shown on the user dashboard.
/// </summary>
public record class UserDeliveryDashboardDto
{
    /// <summary>When the delivery was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>The event type that was delivered.</summary>
    public string EventType { get; set; }
    /// <summary>The name of the receiving subscription.</summary>
    public string SubscriptionName { get; set; }
    /// <summary>The delivery status, as the name of a <see cref="Constants.WebhookDeliveryStatus"/> value.</summary>
    public string Status { get; set; }
    /// <summary>The average HTTP duration of the delivery's attempts, in milliseconds; <see langword="null"/> if none were made.</summary>
    public double? AverageDuration { get; set; }
}
