using WebHook.Core.Constants;

namespace WebHook.Core.Entities;

public class OtpOperationToken
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; } = null!;
    public Guid Jti { get; set; }
    public Guid OtpVerificationId { get; set; }
    public OtpPurpose Purpose { get; set; }
    public string TokenHash { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public OtpVerification OtpVerification { get; set; } = null!;
    public User? UserToPerformOperation { get; set; }
}
