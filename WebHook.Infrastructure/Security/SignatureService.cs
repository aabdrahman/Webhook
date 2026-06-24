using Serilog;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using WebHook.Core.Interfaces.Helpers;

namespace WebHook.Infrastructure.Security;

/// <summary>
/// Implements HMAC-SHA256 payload signing and verification for webhook deliveries.
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

    public SignatureService()
    {
        _logger = Log.Logger.ForContext<SignatureService>().ForContext(_className, nameof(SignatureService));
    }

    ///<inheritdoc />
    public string GenerateSignature(string payLoad, string encryptionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payLoad, nameof(payLoad));
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptionKey, nameof(encryptionKey));

        _logger = _logger.ForContext(_methodName, nameof(GenerateSignature));

        try
        {
            _logger.Information("Generating signature for payload.");

            byte[] payLoadBytes = Encoding.UTF8.GetBytes(payLoad);
            byte[] encryptionKeyBytes = Encoding.UTF8.GetBytes(encryptionKey);

            using var hmac = new HMACSHA256(encryptionKeyBytes);
            byte[] hashBytes = hmac.ComputeHash(payLoadBytes);

            string signature = SignaturePrefix + Convert.ToHexString(hashBytes).ToLowerInvariant();

            _logger.Information("Signature generated successfully.");

            return signature;

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error occurred while generating webhook signature.");
            throw;
        }
    }
    ///<inheritdoc />
    public bool IsTimeStampValid(DateTimeOffset timeStamp, int toleranceWindowInMinutes = 5)
    {
        _logger = _logger.ForContext(_methodName, nameof(IsTimeStampValid));

        try
        {
            _logger.Information("Validating webhook timestamp - tolerance: {0}", timeStamp);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            double ageInMinutes = Math.Abs((now - timeStamp).Minutes);

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

        _logger = _logger.ForContext(_methodName, nameof(VerifySignature));

        try
        {
            _logger.Information("Verifying payload signature.");

            string expectedSignature = GenerateSignature(payLoad, encryptionKey);

            bool isValid = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expectedSignature), Encoding.UTF8.GetBytes(receivedSignature));

            if (isValid)
                _logger.Information("Signature Verified successfully.");
            else
                _logger.Information("Signature Verification Failed - Payload may have been tampered with.");

            return isValid;

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred verifying webhook signature.");
            return false;

        }

    }
}
