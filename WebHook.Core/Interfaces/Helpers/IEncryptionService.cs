namespace WebHook.Core.Interfaces.Helpers;

public interface IEncryptionService
{
    string Encrypt(string valueToEncrypt, string encryptionKey = "");
    string Decrypt(string encryptedValue, string encryptionKey = "");
}
