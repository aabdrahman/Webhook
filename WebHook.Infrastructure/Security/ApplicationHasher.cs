using Serilog;
using System.Security.Cryptography;
using WebHook.Core.Interfaces.Helpers;

namespace WebHook.Infrastructure.Security;

public class ApplicationHasher : IApplicationHasher
{
    private readonly HashAlgorithmName hashAlgorithm = HashAlgorithmName.SHA256;

    private const int iterationCount = 1000000;
    private const int hashSize = 32;
    private const int saltSize = 16;

    public ApplicationHasher()
    {
        _logger = Log.ForContext(_classNmae, nameof(ApplicationHasher));
    }

    private ILogger _logger;
    private const string _methodName = "MethodName";
    private const string _classNmae = "ClassName";

    public async Task<string> HashSecret(string secret)
    {
        _logger = _logger.ForContext(_methodName, nameof(HashSecret));

        try
        {
            await Task.Delay(1);
            byte[] salt = RandomNumberGenerator.GetBytes(saltSize);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterationCount, hashAlgorithm, hashSize);

            return string.Concat(Convert.ToHexString(hash), "-", Convert.ToHexString(salt));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while hashing secret.");
            return string.Empty;
        }
    }

    public async Task<bool> ValidateHashedSecret(string hashedSecret, string secret)
    {
        _logger = _logger.ForContext(_methodName, nameof(ValidateHashedSecret));

        try
        {
            var hashedItems = hashedSecret.Split("-");

            if (hashedItems.Length != 2)
            {
                _logger.Warning("Provided hashed secret has total count of size - {0}", hashedItems.Length);
                return false;
            }

            var saltItem = hashedItems[1];
            var hashedValue = hashedItems[0];

            var saltByte = Convert.FromHexString(saltItem);
            var hashByte = Convert.FromHexString(hashedValue);

            await Task.Delay(1);

            var newHashedSecret = Rfc2898DeriveBytes.Pbkdf2(secret, saltByte, iterationCount, hashAlgorithm, hashSize);

            return CryptographicOperations.FixedTimeEquals(hashByte, newHashedSecret);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while validating hashed secret...");
            return false;
        }

    }
}
