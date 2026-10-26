namespace WebHook.Core.Interfaces.Helpers;

/// <summary>
/// Generates numeric one-time passwords.
/// </summary>
public interface IOtpGenerator
{
    /// <summary>
    /// Generates a random numeric OTP using a cryptographically secure generator.
    /// </summary>
    /// <param name="length">The number of digits. Must be greater than zero.</param>
    /// <param name="maxLength">The largest permitted <paramref name="length"/>.</param>
    /// <returns>The OTP, or an empty string if <paramref name="length"/> is out of range.</returns>
    string GenerateOtp(int length = 6, int maxLength = 12);
}
