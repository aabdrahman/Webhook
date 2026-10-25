namespace WebHook.Core.DataTransferObjects.OtpOperation;

/// <summary>
/// Request to verify a one-time password previously sent to a user's email address.
/// </summary>
public class OtpVerificationRequestDto
{
    /// <summary>The one-time password received by email.</summary>
    public string Otp { get; set; }
    /// <summary>The email address the OTP was sent to.</summary>
    public string EmailAddress { get; set; }
}
