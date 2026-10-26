using Microsoft.AspNetCore.Identity;

namespace WebHook.Core.Entities;

/// <summary>
/// A user of the WebhookHub portal, extending the ASP.NET Core Identity user.
/// </summary>
public class User : IdentityUser<Guid>
{
    /// <summary>When the account was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>The user's first name.</summary>
    public string FirstName { get; set; }
    /// <summary>The user's last name.</summary>
    public string LastName { get; set; }
    /// <summary>Whether the account is active. Deactivated users cannot log in.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When the account was deactivated, or <see langword="null"/> if it is active.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
    /// <summary>The ID of the user (the account owner or an admin) who deactivated the account.</summary>
    public string? DeletedByUserId { get; set; }
    /// <summary>The reason given for deactivating the account.</summary>
    public string? DeactivationJustification { get; set; }

    /// <summary>The current refresh token. Cleared on password change or reset to force a fresh login.</summary>
    public string? RefreshToken { get; set; }
    /// <summary>When the user last logged in or refreshed their token.</summary>
    public DateTimeOffset? LastAuthenticatedAt { get; set; }
    /// <summary>When the user last logged in with their password.</summary>
    public DateTimeOffset? LastLoginDate { get; set; }
    /// <summary>When <see cref="RefreshToken"/> expires.</summary>
    public DateTimeOffset? TokenExpirationTime { get; set; }

    //Relationship with the subscriptions created by the user
    /// <summary>The subscriptions the user created.</summary>
    public ICollection<WebhookSubscription> WebhookSubscriptions { get; set; } = [];

    //Relationship with the otp verificatiosn
    /// <summary>The OTPs issued to the user.</summary>
    public ICollection<OtpVerification> OtpVerifications { get; set; } = [];
    /// <summary>The operation tokens issued to the user.</summary>
    public ICollection<OtpOperationToken> OtpOperationTokens { get; set; } = [];
}
