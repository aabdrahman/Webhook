namespace WebHook.Core.Entities.ConfigurationModels;

/// <summary>
/// Lifetimes of one-time passwords and the operation tokens issued after verifying them.
/// Bound from configuration.
/// </summary>
public class TokenValidationConfiguration
{
    /// <summary>How long, in seconds, an emailed OTP can be verified after it is issued.</summary>
    public double OtpExpirationAfterInSeconds { get; set; }
    /// <summary>
    /// How long, in seconds, the operation token returned by a successful OTP verification
    /// can be used to perform the protected operation.
    /// </summary>
    public double OtpOperationTokenExpiresAFterInSceonds { get; set; }
}
