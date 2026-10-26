namespace WebHook.Core.Interfaces.Helpers;

/// <summary>
/// Generates random secret keys.
/// </summary>
public interface ISecretKeyGenerator
{
    /// <summary>
    /// Generates a random key using a cryptographically secure generator.
    /// </summary>
    /// <param name="secretKeySize">The number of random bytes.</param>
    /// <returns>The key as an upper-case hex string (two characters per byte).</returns>
    string GenerateKey(int secretKeySize = 32);
}
