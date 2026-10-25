using System.Security.Cryptography;
using System.Text;
using WebHook.Core.Interfaces.Helpers;

namespace WebHook.Infrastructure.Security;

/// <summary>
/// <see cref="IEncryptionService"/> implementation using AES-GCM with a random nonce per value.
/// </summary>
/// <remarks>
/// <para>
/// The default key is read, hex-encoded, from the <c>env_webhook_encrypt_key</c> environment
/// variable and must be 16, 24 or 32 bytes. Each call to <see cref="Encrypt"/> generates a new
/// 12-byte nonce, so encrypting the same value twice gives different ciphertexts, and the
/// authentication tag makes any tampering fail on decryption.
/// </para>
/// <para>
/// Values are stored as <c>v2:</c> followed by <c>Base64(nonce | tag | ciphertext)</c>. The nonce is not
/// secret and is kept with the value; only the key must be protected.
/// </para>
/// <para>
/// Values without the <c>v2:</c> prefix were encrypted by the previous AES-CBC implementation with a
/// fixed IV from <c>env_webhook_encrypt_iv</c>. They can still be decrypted, which requires that variable,
/// until every stored secret has been re-encrypted.
/// </para>
/// </remarks>
public sealed class EncryptionService : IEncryptionService
{
    private const string KeyVariableName = "env_webhook_encrypt_key";
    private const string LegacyIvVariableName = "env_webhook_encrypt_iv";

    private const string V2Prefix = "v2:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">
    /// The key is missing, or a legacy value is decrypted while the IV environment variable is missing.
    /// </exception>
    /// <exception cref="CryptographicException">The value was tampered with or the key is wrong.</exception>
    public string Decrypt(string encryptedValue, string encryptionKey = "")
    {
        ArgumentNullException.ThrowIfNullOrEmpty(encryptedValue, nameof(encryptedValue));

        byte[] key = GetKey(encryptionKey);

        if (!encryptedValue.StartsWith(V2Prefix, StringComparison.Ordinal))
            return DecryptLegacyCbc(encryptedValue, key);

        byte[] payload = Convert.FromBase64String(encryptedValue[V2Prefix.Length..]);

        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("The encrypted value is too short to be valid.");

        ReadOnlySpan<byte> nonce = payload.AsSpan(0, NonceSize);
        ReadOnlySpan<byte> tag = payload.AsSpan(NonceSize, TagSize);
        ReadOnlySpan<byte> ciphertext = payload.AsSpan(NonceSize + TagSize);

        byte[] plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">The key is missing.</exception>
    public string Encrypt(string valueToEncrypt, string encryptionKey = "")
    {
        ArgumentNullException.ThrowIfNullOrEmpty(valueToEncrypt, nameof(valueToEncrypt));

        byte[] key = GetKey(encryptionKey);

        byte[] plaintext = Encoding.UTF8.GetBytes(valueToEncrypt);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        byte[] payload = [.. nonce, .. tag, .. ciphertext];

        return V2Prefix + Convert.ToBase64String(payload);
    }

    private static byte[] GetKey(string encryptionKey)
    {
        string? keyToUse = string.IsNullOrWhiteSpace(encryptionKey)
            ? Environment.GetEnvironmentVariable(KeyVariableName)
            : encryptionKey;

        if (string.IsNullOrWhiteSpace(keyToUse))
            throw new ArgumentNullException(nameof(encryptionKey), "Encryption key is missing.");

        return Convert.FromHexString(keyToUse);
    }

    /// <summary>
    /// Decrypts a value written by the previous AES-CBC implementation, which used a fixed IV.
    /// Remove once all stored secrets have been re-encrypted with the <c>v2:</c> format.
    /// </summary>
    private static string DecryptLegacyCbc(string encryptedValue, byte[] key)
    {
        string? encryptionIV = Environment.GetEnvironmentVariable(LegacyIvVariableName);
        if (string.IsNullOrWhiteSpace(encryptionIV))
            throw new ArgumentNullException(LegacyIvVariableName, "Encryption IV is missing.");

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = Convert.FromHexString(encryptionIV);
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using ICryptoTransform cryptoTransform = aes.CreateDecryptor(aes.Key, aes.IV);
        using MemoryStream memoryStream = new MemoryStream(Convert.FromBase64String(encryptedValue));
        using CryptoStream cryptoStream = new CryptoStream(memoryStream, cryptoTransform, CryptoStreamMode.Read);
        using StreamReader streamReader = new StreamReader(cryptoStream, Encoding.UTF8);

        return streamReader.ReadToEnd();
    }
}
