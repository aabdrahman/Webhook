namespace WebHook.Core.Interfaces.Helpers;

public interface ISignatureService
{
    /// <summary>
    /// Verifies that a received signature matches the expected signature
    /// computed from the payload and secret.
    /// </summary>
    /// <param name="payLoad">The raw JSON request body that was received.</param>
    /// <param name="receivedSignature">The signature from the X-Webhook-Signature header.</param>
    /// <param name="encryptionKey">The plaintext shared secret for this webhook subscription.</param>
    /// <returns>True if the signature is valid; otherwise false.</returns>
    bool VerifySignature(string payLoad, string receivedSignature, string encryptionKey);
    /// <summary>
    /// Generates an HMAC-SHA256 signature for the given payload using the provided secret.
    /// </summary>
    /// <param name="payLoad">The raw JSON request body to sign</param>
    /// <param name="encryptionKey">The plaintext shared secret for this webhook subscription.</param>
    /// <returns>A hex-encoded HMAC-SHA256 signature string.</returns>
    string GenerateSignature(string payLoad, string encryptionKey);
    /// <summary>
    /// Verifies that the timestamp on a received webhook request falls within
    /// the accepted tolerance window, protecting against replay attacks.
    /// </summary>
    /// <param name="timeStamp">The value from the X-Webhook-Timestamp header (ISO 8601).</param>
    /// <param name="toleranceWindowInMinutes">How many minutes old a request is allowed to be. Defaults to 5.</param>
    /// <returns>True if the timestamp is within the tolerance window; otherwise false.</returns>
    bool IsTimeStampValid(DateTimeOffset timeStamp, int toleranceWindowInMinutes = 5);
}
