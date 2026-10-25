using WebHook.Core.Constants;

namespace WebHook.Core.Entities;

/// <summary>
/// A one-time password emailed to a user for a sensitive operation.
/// </summary>
/// <remarks>
/// Only a hash of the OTP is stored. Verifying it sets <see cref="ValidatedAt"/> and issues an
/// <see cref="OtpOperationToken"/>; completing the operation marks it consumed.
/// </remarks>
public class OtpVerification
{
    /// <summary>The primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>The ID of the user the OTP was sent to.</summary>
    public Guid? UserId { get; set; } = null!;
    /// <summary>The operation the OTP was requested for.</summary>
    public OtpPurpose Purpose { get; set; }
    /// <summary>The hash of the OTP.</summary>
    public string OtpHash { get; set; } = null!;
    /// <summary>When the OTP was issued.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>When the OTP expires.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>When the OTP was successfully verified, or <see langword="null"/> if it has not been.</summary>
    public DateTimeOffset? ValidatedAt { get; set; }
    /// <summary>The number of failed verification attempts. Not currently incremented.</summary>
    public int FailedAttemptCount { get; set; } = 0;
    /// <summary>Whether the operation the OTP authorised has been completed.</summary>
    public bool IsConsumed { get; set; } = false;
    /// <summary>When the OTP was consumed, or <see langword="null"/> if it has not been.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }
    /// <summary>When the OTP was revoked through the revoke OTP endpoint, or <see langword="null"/> if it has not been.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
    /// <summary>The operation tokens issued from this OTP.</summary>
    public ICollection<OtpOperationToken> OperationTokens { get; set; } = [];
    /// <summary>The user the OTP was sent to.</summary>
    public User? UserToVerify { get; set; }
}
