namespace WebHook.Core.DataTransferObjects.OtpOperation;

/// <summary>
/// Result of a successful OTP verification: a short-lived operation token
/// that authorises the sensitive operation the OTP was requested for.
/// </summary>
public record class OtpVerificationDto
{
    /// <summary>
    /// The data-protected operation token. Supply it to the operation the OTP
    /// was issued for (for example password reset or profile deactivation).
    /// </summary>
    public string SignedToken { get; set; }
    /// <summary>When the operation token stops being accepted.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
