namespace Webhook.Web.Components.TrendAnalytics.Models;

// DailyVolumeDto.cs
public sealed record DailyVolumeDto(
    DateOnly Date,
    long Total,
    long Success,
    long Failed
);

// SuccessRateTrendDto.cs
public sealed record SuccessRateTrendDto(
    DateOnly Date,
    decimal SuccessRate
);

// FailureByCodeDto.cs
public sealed record FailureByCodeDto(
    string CodeGroup,   // "5xx", "4xx", "Timeout", "No Response"
    int Count
);

// DeadLetterRateDto.cs
public sealed record DeadLetterRateDto(
    DateOnly Date,
    long Count
);

// EventTypeDistributionDto.cs
public sealed record EventTypeDistributionDto(
    string EventType,
    long Count,
    decimal Percentage
);

// LatencyPercentilesDto.cs
public sealed record LatencyPercentilesDto(
    double P50Ms,
    double P75Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs
);

// DeliveriesPerSubDto.cs
public sealed record DeliveriesPerSubDto(
    string SubscriptionName,
    long Total,
    long Failed
);

// DeliveryFeedItemDto.cs
public sealed record DeliveryFeedItemDto(
    string EventType,
    string SubscriptionName,
    string HttpResponseCode,
    DateTime OccurredAt
);

// DateRange.cs
public sealed record DateRange(DateOnly Start, DateOnly End)
{
    public static DateRange Last7Days() => new(DateOnly.FromDateTime(DateTime.Today.AddDays(-7)), DateOnly.FromDateTime(DateTime.Today));
    public static DateRange Last30Days() => new(DateOnly.FromDateTime(DateTime.Today.AddDays(-30)), DateOnly.FromDateTime(DateTime.Today));
    public static DateRange Last90Days() => new(DateOnly.FromDateTime(DateTime.Today.AddDays(-90)), DateOnly.FromDateTime(DateTime.Today));
}

//Enum Constants
public enum TrendDirection { Up, Down }
public enum KpiAccent { Blue, Green, Amber, Red }