using System;
using System.Collections.Generic;
using System.Text;

namespace WebHook.Core.DataTransferObjects.ReportingAnalytics;

public record class UserDeliveryDashboardDto
{
    public DateTimeOffset CreatedAt { get; set; }
    public string EventType { get; set; }
    public string SubscriptionName { get; set; }
    public string Status { get; set; }
    public double? AverageDuration { get; set; }
}
