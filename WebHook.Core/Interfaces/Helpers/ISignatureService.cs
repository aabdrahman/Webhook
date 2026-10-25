namespace WebHook.Core.Interfaces.Helpers;

/// <summary>
/// Signs webhook payloads so subscribers can verify they came from WebhookHub
/// and were not modified in transit.
/// </summary>
/// <remarks>
/// Deliveries are signed with the timestamped overloads: the HMAC covers
/// <c>"{timestamp}.{payload}"</c>, where <c>timestamp</c> is the value of the
/// <c>X-Webhook-Timestamp</c> header (Unix seconds). Binding the timestamp into the
/// signature stops a captured request from being replayed with a fresh timestamp.
/// The HMAC key is the UTF-8 bytes of the subscription's secret string.
/// </remarks>
public interface ISignatureService
{
    /// <summary>
    /// Generates the HMAC-SHA256 signature for a delivery, covering both the timestamp and the payload.
    /// </summary>
    /// <param name="payLoad">The raw JSON request body to sign.</param>
    /// <param name="encryptionKey">The plaintext shared secret for this webhook subscription.</param>
    /// <param name="timestamp">The Unix time, in seconds, sent in the <c>X-Webhook-Timestamp</c> header.</param>
    /// <returns>
    /// The signature in the form <c>sha256=&lt;lower-case hex HMAC of "{timestamp}.{payload}"&gt;</c>,
    /// as sent in the <c>X-Webhook-Signature</c> header.
    /// </returns>
    string GenerateSignature(string payLoad, string encryptionKey, long timestamp);
    /// <summary>
    /// Verifies a delivery signature produced by <see cref="GenerateSignature(string, string, long)"/>, in constant time.
    /// </summary>
    /// <remarks>
    /// This checks the signature only. Call <see cref="IsTimeStampValid"/> as well to reject stale requests.
    /// </remarks>
    /// <param name="payLoad">The raw JSON request body that was received.</param>
    /// <param name="receivedSignature">The signature from the <c>X-Webhook-Signature</c> header.</param>
    /// <param name="encryptionKey">The plaintext shared secret for this webhook subscription.</param>
    /// <param name="timestamp">The Unix time, in seconds, from the <c>X-Webhook-Timestamp</c> header.</param>
    /// <returns>True if the signature is valid; otherwise false.</returns>
    bool VerifySignature(string payLoad, string receivedSignature, string encryptionKey, long timestamp);
    /// <summary>
    /// Verifies that a received signature matches the expected signature
    /// computed from the payload and secret alone.
    /// </summary>
    /// <remarks>
    /// Payload-only signatures do not protect against replay. Deliveries use
    /// <see cref="VerifySignature(string, string, string, long)"/>.
    /// </remarks>
    /// <param name="payLoad">The raw JSON request body that was received.</param>
    /// <param name="receivedSignature">The signature from the X-Webhook-Signature header.</param>
    /// <param name="encryptionKey">The plaintext shared secret for this webhook subscription.</param>
    /// <returns>True if the signature is valid; otherwise false.</returns>
    bool VerifySignature(string payLoad, string receivedSignature, string encryptionKey);
    /// <summary>
    /// Generates an HMAC-SHA256 signature for the given payload using the provided secret.
    /// </summary>
    /// <remarks>
    /// Payload-only signatures do not protect against replay. Deliveries use
    /// <see cref="GenerateSignature(string, string, long)"/>.
    /// </remarks>
    /// <param name="payLoad">The raw JSON request body to sign</param>
    /// <param name="encryptionKey">The plaintext shared secret for this webhook subscription.</param>
    /// <returns>The signature in the form <c>sha256=&lt;lower-case hex HMAC&gt;</c>, as sent in the <c>X-Webhook-Signature</c> header.</returns>
    string GenerateSignature(string payLoad, string encryptionKey);
    /// <summary>
    /// Verifies that the timestamp on a received webhook request falls within
    /// the accepted tolerance window, protecting against replay attacks.
    /// </summary>
    /// <param name="timeStamp">
    /// The time from the <c>X-Webhook-Timestamp</c> header, which is sent as Unix seconds; convert it with
    /// <see cref="DateTimeOffset.FromUnixTimeSeconds"/>. Timestamps in the future are accepted within the same window.
    /// </param>
    /// <param name="toleranceWindowInMinutes">How many minutes old a request is allowed to be. Defaults to 5.</param>
    /// <returns>True if the timestamp is within the tolerance window; otherwise false.</returns>
    bool IsTimeStampValid(DateTimeOffset timeStamp, int toleranceWindowInMinutes = 5);
}
