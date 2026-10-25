using Serilog;
using System.Security.Cryptography;
using System.Text;
using WebHook.Core.Interfaces.Helpers;

namespace WebHook.Infrastructure.Security;

/// <summary>
/// Implements HMAC-SHA256 payload signing and verification for webhook deliveries.
/// Delivery signatures cover <c>"{timestamp}.{payload}"</c>; see <see cref="ISignatureService"/>.
/// Lives in Infrastructure because it uses <see cref="HMACSHA256"/> from the BCL
/// and has no domain logic — it is purely a security utility.
/// </summary>
public sealed class SignatureService : ISignatureService
{
    private const string _className = "ClassName";
    private const string _methodName = "MethodName";

    private ILogger _logger;

    // Prefix added to the header value so consumers can parse it unambiguously:
    // X-Webhook-Signature: sha256=<hex>
    private const string SignaturePrefix = "sha256=";

    /// <summary>
    /// Initialises a new instance of the <see cref="SignatureService"/> class.
    /// </summary>
    public SignatureService()
    {
        _logger = Log.Logger.ForContext<SignatureService>().ForContext(_className, nameof(SignatureService));
    }

    ///<inheritdoc />
    public string GenerateSignature(string payLoad, string encryptionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payLoad, nameof(payLoad));
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptionKey, nameof(encryptionKey));

        return ComputeSignature(payLoad, encryptionKey);
    }

    ///<inheritdoc />
    public string GenerateSignature(string payLoad, string encryptionKey, long timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payLoad, nameof(payLoad));
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptionKey, nameof(encryptionKey));

        return ComputeSignature(BuildSignedContent(payLoad, timestamp), encryptionKey);
    }

    ///<inheritdoc />
    public bool VerifySignature(string payLoad, string receivedSignature, string encryptionKey, long timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payLoad, nameof(payLoad));
        ArgumentException.ThrowIfNullOrWhiteSpace(receivedSignature, nameof(receivedSignature));
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptionKey, nameof(encryptionKey));

        return IsMatch(ComputeSignature(BuildSignedContent(payLoad, timestamp), encryptionKey), receivedSignature);
    }

    // The signed content binds the timestamp to the payload: "{timestamp}.{payload}".
    private static string BuildSignedContent(string payLoad, long timestamp) => $"{timestamp}.{payLoad}";

    private string ComputeSignature(string content, string encryptionKey)
    {
        ILogger logger = _logger.ForContext(_methodName, nameof(GenerateSignature));

        try
        {
            byte[] contentBytes = Encoding.UTF8.GetBytes(content);
            byte[] encryptionKeyBytes = Encoding.UTF8.GetBytes(encryptionKey);

            using var hmac = new HMACSHA256(encryptionKeyBytes);
            byte[] hashBytes = hmac.ComputeHash(contentBytes);

            return SignaturePrefix + Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error occurred while generating webhook signature.");
            throw;
        }
    }

    private bool IsMatch(string expectedSignature, string receivedSignature)
    {
        bool isValid = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expectedSignature), Encoding.UTF8.GetBytes(receivedSignature));

        if (!isValid)
            _logger.ForContext(_methodName, nameof(VerifySignature)).Information("Signature Verification Failed - Payload may have been tampered with.");

        return isValid;
    }
    ///<inheritdoc />
    public bool IsTimeStampValid(DateTimeOffset timeStamp, int toleranceWindowInMinutes = 5)
    {
        _logger = _logger.ForContext(_methodName, nameof(IsTimeStampValid));

        try
        {
            _logger.Information("Validating webhook timestamp - tolerance: {0}", timeStamp);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            double ageInMinutes = Math.Abs((now - timeStamp).TotalMinutes);

            bool isValid = ageInMinutes <= toleranceWindowInMinutes;

            if (isValid)
                _logger.Information("Timestamp is within acceptable window - {0} minutes old.", ageInMinutes);
            else
                _logger.Information("Timestamp rejected - request is {0} minutes old, tolerance window: {1}", ageInMinutes, toleranceWindowInMinutes);

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred validating timestamp for: {0}. WIndow: {1}", timeStamp, toleranceWindowInMinutes);
            return false;

        }
    }
    ///<inheritdoc />
    public bool VerifySignature(string payLoad, string receivedSignature, string encryptionKey)
    {

        ArgumentException.ThrowIfNullOrWhiteSpace(payLoad, nameof(payLoad));
        ArgumentException.ThrowIfNullOrWhiteSpace(receivedSignature, nameof(receivedSignature));
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptionKey, nameof(encryptionKey));

        try
        {
            return IsMatch(ComputeSignature(payLoad, encryptionKey), receivedSignature);
        }
        catch (Exception ex)
        {
            _logger.ForContext(_methodName, nameof(VerifySignature)).Error(ex, "An error occurred verifying webhook signature.");
            return false;

        }

    }
}
