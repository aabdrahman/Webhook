namespace WebHook.Core.DataTransferObjects.OtpOperation;

/// <summary>
/// Payload of an OTP operation token. It is serialised to JSON and encrypted
/// with ASP.NET Core Data Protection to produce <see cref="OtpVerificationDto.SignedToken"/>.
/// </summary>
public class OtpVerificationSigning
{
    /// <summary>The unique token identifier, used to look up and invalidate the token server-side.</summary>
    public string Jti { get; set; }
    /// <summary>The normalised email address of the user the token was issued to.</summary>
    public string IssuedFor { get; set; }
    /// <summary>When the token was issued.</summary>
    public DateTimeOffset IssuedAt { get; set; }
    /// <summary>When the token expires.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}