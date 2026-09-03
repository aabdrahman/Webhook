using System.ComponentModel.DataAnnotations;

namespace WebHook.Core.DataTransferObjects.WebhookDeadLetterQueue;

public record class RequestManualRetryDto
{
    [Required(ErrorMessage = "Delivery Id is a required field.")]
    public Guid DeliveryId { get; set; }
    [Required(ErrorMessage = "Dead Letter Id is a required field.")]
    public Guid DeadLetterId { get; set; }
    [Required(ErrorMessage = "The justification for retry is required.")]
    [StringLength(maximumLength: 500, ErrorMessage = "Retry Justification cannot exceed 500 chanracters")]
    public string RetryJustification { get; set; }
}


public record DeadLetterQueueDto(Guid id, DateTimeOffset createdAt, string reason, DateTimeOffset? RetriedAt, string? RetryJustification, string? retriedBy);