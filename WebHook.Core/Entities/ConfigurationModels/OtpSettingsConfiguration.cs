namespace WebHook.Core.Entities.ConfigurationModels;

/// <summary>
/// Settings that control one-time password generation. Bound from configuration.
/// </summary>
public class OtpSettingsConfiguration
{
    /// <summary>
    /// The upper limit on OTP length. Generation fails if <see cref="OtpToGenerateLength"/> exceeds it.
    /// </summary>
    public int MaximumOtpLength { get; set; }
    /// <summary>The number of digits in each generated OTP.</summary>
    public int OtpToGenerateLength { get; set; }
}
