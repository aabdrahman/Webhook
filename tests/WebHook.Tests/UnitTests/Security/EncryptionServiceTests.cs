using System.Security.Cryptography;
using WebHook.Infrastructure.Security;

namespace WebHook.Tests.UnitTests.Security;

public sealed class EncryptionServiceTests : IDisposable
{
    private readonly EncryptionService _sut;

    public EncryptionServiceTests()
    {
        Environment.SetEnvironmentVariable("env_webhook_encrypt_key", "00AF7F388E47B0AC211E931842DAFA7422648881071244DF484725D249D9E5F1");
        Environment.SetEnvironmentVariable("env_webhook_encrypt_iv", "315BDF36FBC81E00D668494E187AC4A5");

        _sut = new EncryptionService();
    }

    [Fact]
    public void EncryptionService_NullValueToEncrypt_ThrowsException()
    {
        //Arrange
        string? plainText = string.Empty;

        //Act

        //Assert
        Assert.Throws<ArgumentException>(() => _sut.Encrypt(plainText));
    }

    [Fact]
    public void EncryptionService_NullValueToDecrypt_ThrowsException()
    {
        //Arrange
        string encryptedText = string.Empty;
        //Act

        //Assert
        Assert.Throws<ArgumentException>(() => _sut.Decrypt(encryptedText));
    }

    [Fact]
    public void EncryptionService_NullEncryptionKey_Encrypt_ShouldArgumentNullThrowException()
    {
        //Arrange
        Environment.SetEnvironmentVariable("env_webhook_encrypt_key", null);
        string valueToEncrypt = "Webhook Test";

        //Act

        //Assert
        Assert.Throws<ArgumentNullException>(() => _sut.Encrypt(valueToEncrypt));
    }

    [Fact]
    public void EncryptionService_NullEncryptionIV_Encrypt_ShouldArgumentNullThrowException()
    {
        //Arrange
        Environment.SetEnvironmentVariable("env_webhook_encrypt_iv", null);
        string valueToEncrypt = "Webhook Test";

        //Act

        //Assert
        Assert.Throws<ArgumentNullException>(() => _sut.Encrypt(valueToEncrypt));
    }

    [Fact]
    public void EncryptionService_NullEncryptionKey_Decrypt_ShouldArgumentNullThrowException()
    {
        //Arrange
        Environment.SetEnvironmentVariable("env_webhook_encrypt_key", null);
        string valueToEncrypt = "Webhook Test";

        //Act

        //Assert
        Assert.Throws<ArgumentNullException>(() => _sut.Decrypt(valueToEncrypt));
    }

    [Fact]
    public void EncryptionService_NullEncryptionIV_Decrypt_ShouldArgumentNullThrowException()
    {
        //Arrange
        Environment.SetEnvironmentVariable("env_webhook_encrypt_iv", null);
        string valueToEncrypt = "Webhook Test";

        //Act

        //Assert
        Assert.Throws<ArgumentNullException>(() => _sut.Decrypt(valueToEncrypt));
    }

    [Fact]
    public void EncryptionService_Should_Return_Encrypted_Value()
    {
        //Arrange
        string valueToEncrypt = "Webhook Test";

        //Act
        var encryptedValue = _sut.Encrypt(valueToEncrypt);

        //Assert
        Assert.False(string.IsNullOrEmpty(encryptedValue));
        Assert.NotEqual(encryptedValue, valueToEncrypt);
    }

    [Fact]
    public void EncryptionService_Encrypt_Then_Decrypt_Should_Return_Original_Value()
    {
        //Arrange
        string valueToEncrypt = "Webhook Test";

        //Act
        var encryptedValue = _sut.Encrypt(valueToEncrypt);
        var decryptedValue = _sut.Decrypt(encryptedValue);

        //Assert
        Assert.Equal(valueToEncrypt, decryptedValue);
    }

    [Fact]
    public void EncryptionService_Encrypt_Should_Use_Correct_Key()
    {
        //Arrange
        string valueToEncrypt = "Webhook Test";
        string customKey = "2C1115238CE3BAB1149B5F31D45C0DD606C4AA573A8EA2CA3845E63BD891FA81";

        //Act
        var encryptedValue = _sut.Encrypt(valueToEncrypt, customKey);
        var decryptedValue = _sut.Decrypt(encryptedValue, customKey);

        //Assert
        Assert.Equal(decryptedValue, valueToEncrypt);
    }

    [Fact]
    public void EncryptionService_Encrypt_Should_Fail_For_Wrong_Key()
    {
        //Arrange
        string customKey = "E9E567A40387144CC240711628E34C61699B2C0D5441A3B409EB686F8AB9D7A2";
        string wrongKey = "4372E0FF562A625335C99E9DBD66488D1115DA254059C791E173C49222DA48C3";
        string valueToEncrypt = "Webhook Test";
        //Act
        var encryptedValue = _sut.Encrypt(valueToEncrypt, customKey);
        // var decryptedValue = _sut.Decrypt(encryptedValue, wrongKey);

        //Assert
        Assert.Throws<CryptographicException>(() => _sut.Decrypt(encryptedValue, wrongKey));
    }

    public void Dispose()
    {
        //throw new NotImplementedException();
    }
}
