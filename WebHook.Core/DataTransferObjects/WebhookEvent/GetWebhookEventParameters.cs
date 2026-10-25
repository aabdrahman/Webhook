namespace WebHook.Core.DataTransferObjects.WebhookEvent;

/// <summary>
/// Filter parameters for listing webhook events. The date range is always applied;
/// the other filters are applied only when provided.
/// </summary>
public record class GetWebhookEventParameters
{
    /// <summary>Filters by event type (case-insensitive).</summary>
    public string? EventType { get; init; }
    /// <summary>Filters by the originating service (exact match).</summary>
    public string? Source { get; init; }
    /// <summary>
    /// Filters by the name of a <see cref="Constants.WebHookEventStatus"/> value (case-insensitive).
    /// Unrecognised values are ignored.
    /// </summary>
    public string? Status { get; init; }
    /// <summary>Filters by correlation ID.</summary>
    public Guid? CorrelationId { get; init; }
    /// <summary>The inclusive start of the creation date range. Defaults to three days ago.</summary>
    public DateTimeOffset? CreatedAtFrom { get; init; } = DateTimeOffset.UtcNow.AddDays(-3);
    /// <summary>The inclusive end of the creation date range. Defaults to now.</summary>
    public DateTimeOffset? CreatedAtTo { get; init; } = DateTimeOffset.UtcNow;
}