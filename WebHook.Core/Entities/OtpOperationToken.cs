using WebHook.Core.Constants;

namespace WebHook.Core.Entities;

/// <summary>
/// A short-lived operation token issued after an OTP is verified. Presenting it
/// authorises the sensitive operation named by <see cref="Purpose"/>.
/// </summary>
/// <remarks>
/// Only a hash of the token is stored. A token is usable while it is unexpired,
/// not consumed and not revoked.
/// </remarks>
public class OtpOperationToken
{
    /// <summary>The primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>The ID of the user the token was issued to.</summary>
    public Guid? UserId { get; set; } = null!;
    /// <summary>The unique token identifier embedded in the protected token payload.</summary>
    public Guid Jti { get; set; }
    /// <summary>The ID of the OTP verification that produced this token.</summary>
    public Guid OtpVerificationId { get; set; }
    /// <summary>The operation the token authorises.</summary>
    public OtpPurpose Purpose { get; set; }
    /// <summary>The hash of the issued token.</summary>
    public string TokenHash { get; set; } = null!;
    /// <summary>When the token was issued.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>When the token expires.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>When the token was used, or <see langword="null"/> if it has not been.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }
    /// <summary>When the token was revoked, or <see langword="null"/> if it has not been.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
    /// <summary>The OTP verification that produced this token.</summary>
    public OtpVerification OtpVerification { get; set; } = null!;
    /// <summary>The user the token was issued to.</summary>
    public User? UserToPerformOperation { get; set; }
}
