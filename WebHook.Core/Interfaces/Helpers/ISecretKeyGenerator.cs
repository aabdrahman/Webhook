namespace WebHook.Core.Interfaces.Helpers;

public interface ISecretKeyGenerator
{
    string GenerateKey(int secretKeySize = 32);
}
