using System.Security.Cryptography;
using WebHook.Core.Interfaces.Helpers;

namespace WebHook.Infrastructure.Security;

public sealed class SecretKeyGeneratorService : ISecretKeyGenerator
{
    public string GenerateKey(int secretKeySize = 32)
    {
        var rndNumBytes = new byte[secretKeySize];

        using (var randNum = RandomNumberGenerator.Create())
        {
            randNum.GetBytes(rndNumBytes);
        }

        return Convert.ToHexString(rndNumBytes);
    }
}
