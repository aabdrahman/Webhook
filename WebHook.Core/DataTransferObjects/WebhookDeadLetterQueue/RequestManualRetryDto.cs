using System.ComponentModel.DataAnnotations;

namespace WebHook.Core.DataTransferObjects.WebhookDeadLetterQueue;

/// <summary>
/// An admin's request to retry a dead-lettered delivery.
/// </summary>
public record class RequestManualRetryDto
{
    /// <summary>The ID of the dead-lettered delivery.</summary>
    [Required(ErrorMessage = "Delivery Id is a required field.")]
    public Guid DeliveryId { get; set; }
    /// <summary>The ID of the dead-letter queue entry for the delivery.</summary>
    [Required(ErrorMessage = "Dead Letter Id is a required field.")]
    public Guid DeadLetterId { get; set; }
    /// <summary>Why the delivery is being retried. Recorded for audit; at most 500 characters.</summary>
    [Required(ErrorMessage = "The justification for retry is required.")]
    [StringLength(maximumLength: 500, ErrorMessage = "Retry Justification cannot exceed 500 chanracters")]
    public string RetryJustification { get; set; }
}


/// <summary>
/// A dead-letter queue entry for a delivery.
/// </summary>
/// <param name="id">The dead-letter entry ID.</param>
/// <param name="createdAt">When the delivery was dead-lettered.</param>
/// <param name="reason">Why the delivery was dead-lettered.</param>
/// <param name="RetriedAt">When an admin last requested a manual retry, if ever.</param>
/// <param name="RetryJustification">The justification given for the manual retry, if any.</param>
/// <param name="retriedBy">Who requested the manual retry, if anyone.</param>
public record DeadLetterQueueDto(Guid id, DateTimeOffset createdAt, string reason, DateTimeOffset? RetriedAt, string? RetryJustification, string? retriedBy);