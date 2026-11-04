namespace WebHook.Core.DataTransferObjects.SubscriptionVerification;

public record SubscriptionVerificationDto
(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset? AttemptedAt, string Status);
