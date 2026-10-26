namespace WebHook.Core.Constants;

/// <summary>
/// Identifies the sensitive operation a one-time password (OTP) was issued for.
/// </summary>
/// <remarks>
/// The purpose is stored with the OTP and the operation token issued after
/// successful verification. Consuming operations check it so that a token
/// obtained for one purpose cannot be replayed for another.
/// </remarks>
public enum OtpPurpose
{
    /// <summary>The OTP authorises resetting a forgotten password.</summary>
    PasswordReset,
    /// <summary>The OTP authorises changing the email address on the account.</summary>
    ChangeEmail,
    /// <summary>The OTP authorises a user deactivating their own profile.</summary>
    DeactivateProfile
}