using WebHook.Core.Constants;

namespace WebHook.Core.Entities;

public class OtpVerification
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; } = null!;
    public OtpPurpose Purpose { get; set; }
    public string OtpHash { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ValidatedAt { get; set; }
    public int FailedAttemptCount { get; set; } = 0;
    public bool IsConsumed { get; set; } = false;
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public ICollection<OtpOperationToken> OperationTokens { get; set; } = [];
    public User? UserToVerify { get; set; }
}
