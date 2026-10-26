namespace WebHook.Core.Interfaces.Helpers;

/// <summary>
/// Reversible symmetric encryption for secrets that must be recovered later,
/// such as subscription signing secrets.
/// </summary>
public interface IEncryptionService
{
    /// <summary>
    /// Encrypts a value.
    /// </summary>
    /// <param name="valueToEncrypt">The plaintext to encrypt. Must not be empty.</param>
    /// <param name="encryptionKey">A hex-encoded key. When empty, the application's configured key is used.</param>
    /// <returns>The Base64-encoded ciphertext.</returns>
    string Encrypt(string valueToEncrypt, string encryptionKey = "");
    /// <summary>
    /// Decrypts a value produced by <see cref="Encrypt"/>.
    /// </summary>
    /// <param name="encryptedValue">The Base64-encoded ciphertext. Must not be empty.</param>
    /// <param name="encryptionKey">A hex-encoded key. When empty, the application's configured key is used.</param>
    /// <returns>The plaintext.</returns>
    string Decrypt(string encryptedValue, string encryptionKey = "");
}
