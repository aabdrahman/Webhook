using System.Security.Cryptography;
using System.Text;
using WebHook.Core.Interfaces.Helpers;

namespace WebHook.Infrastructure.Security;

public sealed class EncryptionService : IEncryptionService
{
    public string Decrypt(string encryptedValue, string encryptionKey = "")
    {
        ArgumentNullException.ThrowIfNullOrEmpty(encryptedValue, nameof(encryptedValue));

        string? encryptionKeyToUse = string.IsNullOrWhiteSpace(encryptionKey) ? Environment.GetEnvironmentVariable("env_webhook_encrypt_key") : encryptionKey;

        if (string.IsNullOrEmpty(encryptionKeyToUse))
            throw new ArgumentNullException("Encryption Key could not be fetched.");

        var encryptionIV = Environment.GetEnvironmentVariable("env_webhook_encrypt_iv");
        if (string.IsNullOrWhiteSpace(encryptionIV))
            throw new ArgumentNullException("Encryption IV is missing.");


        byte[] encryptyIvByte = Convert.FromHexString(encryptionIV);
        byte[] encryptionKeyByte = Convert.FromHexString(encryptionKeyToUse);

        using var aes = Aes.Create();
        aes.Key = encryptionKeyByte;
        aes.IV = encryptyIvByte;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        ICryptoTransform cryptoTransform = aes.CreateDecryptor(aes.Key, aes.IV);

        using MemoryStream memoryStream = new MemoryStream(Convert.FromBase64String(encryptedValue));
        using CryptoStream cryptoStream = new CryptoStream(memoryStream, cryptoTransform, CryptoStreamMode.Read);
        using StreamReader streamReader = new StreamReader(cryptoStream, Encoding.UTF8);
        {
            return streamReader.ReadToEnd();
        }

    }

    public string Encrypt(string valueToEncrypt, string encryptionKey = "")
    {
        ArgumentNullException.ThrowIfNullOrEmpty(valueToEncrypt, nameof(encryptionKey));

        var encryptKeyDefault = Environment.GetEnvironmentVariable("env_webhook_encrypt_key");
        var encryptionIV = Environment.GetEnvironmentVariable("env_webhook_encrypt_iv");

        var keyToUse = string.IsNullOrWhiteSpace(encryptionKey)
            ? encryptKeyDefault
            : encryptionKey;

        if (string.IsNullOrWhiteSpace(keyToUse))
            throw new ArgumentNullException("Encryption key is missing.");

        if (string.IsNullOrWhiteSpace(encryptionIV))
            throw new ArgumentNullException("Encryption IV is missing.");

        byte[] encryptyIvByte = Convert.FromHexString(encryptionIV);
        byte[] encryptionKeyByte = Convert.FromHexString(keyToUse);

        using (var encryptAes = Aes.Create())
        {
            encryptAes.Key = encryptionKeyByte;
            encryptAes.IV = encryptyIvByte;
            encryptAes.Mode = CipherMode.CBC;
            encryptAes.Padding = PaddingMode.PKCS7;

            ICryptoTransform cryptoTransform = encryptAes.CreateEncryptor(encryptAes.Key, encryptAes.IV);

            using (MemoryStream memoryStream = new MemoryStream())
            using(CryptoStream cryptoStream = new CryptoStream(memoryStream, cryptoTransform, CryptoStreamMode.Write))
            {
                byte[] valueToEncryptByte = Encoding.UTF8.GetBytes(valueToEncrypt);
                cryptoStream.Write(valueToEncryptByte, 0, valueToEncryptByte.Length);
                cryptoStream.FlushFinalBlock();
                return Convert.ToBase64String(memoryStream.ToArray());
            }
        }
    }

}
