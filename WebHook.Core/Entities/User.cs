using Microsoft.AspNetCore.Identity;

namespace WebHook.Core.Entities;

public class User : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? DeactivationJustification { get; set; }

    public string? RefreshToken { get; set; }
    public DateTimeOffset? LastAuthenticatedAt { get; set; }
    public DateTimeOffset? LastLoginDate { get; set; }
    public DateTimeOffset? TokenExpirationTime { get; set; }

    //Relationship with the subscriptions created by the user
    public ICollection<WebhookSubscription> WebhookSubscriptions { get; set; } = [];

    //Relationship with the otp verificatiosn
    public ICollection<OtpVerification> OtpVerifications { get; set; } = [];
    public ICollection<OtpOperationToken> OtpOperationTokens { get; set; } = [];
}
