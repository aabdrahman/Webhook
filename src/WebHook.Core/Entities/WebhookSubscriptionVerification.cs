using WebHook.Core.Constants;

namespace WebHook.Core.Entities;

public class WebhookSubscriptionVerification
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string ChallengeToken { get; set; }
    public DateTimeOffset? AttemptedAt { get; set; }
    public DateTimeOffset? ValidatedAt { get; set; }
    public string ResponseDetails { get; set; } = string.Empty;
    public string ResponseStatusCode { get; set; } = string.Empty;
    public WebhookSubscriptionVerificationStatus Status { get; set; } = WebhookSubscriptionVerificationStatus.Pending;

    //Relationships
    ///<summary> Relationship with WebhookSubscription <see cref="WebhookSubscription"/> </summary>
    public Guid SubscriptionId { get; set; }
    public WebhookSubscription Subscription { get; set; }
}
