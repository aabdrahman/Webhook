using Serilog;
using WebHook.Infrastructure.Security;

namespace WebHook.Tests.UnitTests.Security;

public class SignatureServiceTests
{
    private readonly SignatureService _sut;
    public SignatureServiceTests()
    {
        Log.Logger = new LoggerConfiguration().CreateLogger();
        _sut = new SignatureService();
    }

    [Fact]
    public void GenerateSignature_EmptyPayload_ShouldThrowException()
    {
        //Arrange

        string encryptionKey = "my-secret";

        //Act & Assert
        Assert.Throws<ArgumentException>(() => _sut.GenerateSignature("", encryptionKey));
    }

    [Fact]
    public void GenerateSignature_NullPayload_ShouldThrowException()
    {
        //Arrange

        string encryptionKey = "my-secret";

        //Act & Assert
        Assert.Throws<ArgumentNullException>(() => _sut.GenerateSignature(null!, encryptionKey));
    }

    [Fact]
    public void GenerateSignature_EmptyEncryptionKey_ShouldThrowException()
    {
        //Arrange
        string payload = @"{""eventType"":""CustomerCreated""}";
        string encryptionKey = string.Empty;

        //Act & Assert
        Assert.Throws<ArgumentException>(() => _sut.GenerateSignature(payload, ""));
    }

    [Fact]
    public void GenerateSignature_NullEncryptionKey_ShouldThrowException()
    {
        //Arrange
        string payload = @"{""eventType"":""CustomerCreated""}";

        //Act & Assert
        Assert.Throws<ArgumentNullException>(() => _sut.GenerateSignature(payload, null!));
    }

    [Fact]
    public void GenerateSignature_SamePayloadAndEncryptionKey_GnerateSameSignature()
    {
        //Arrange
        string payload1 = @"{""eventType"":""CustomerCreated""}";
        string payload2 = @"{""eventType"":""CustomerCreated""}";
        string encryptionKey = "default-secret";

        //Act
        var result1 = _sut.GenerateSignature(payload1, encryptionKey);
        var result2 = _sut.GenerateSignature(payload2, encryptionKey);

        //Assert
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void GenerateSignature_DifferentPayloadSameEncryptionKey_GnerateDifferentSignature()
    {
        //Arrange
        string payload1 = @"{""eventType"":""CustomerCreated""}";
        string payload2 = @"{""eventType"":""UserRegistered""}";
        string encryptionKey = "default-secret";

        //Act
        var result1 = _sut.GenerateSignature(payload1, encryptionKey);
        var result2 = _sut.GenerateSignature(payload2, encryptionKey);

        //Assert
        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void GenerateSignature_SamePayloadDifferentEncryptionKey_GnerateDifferentSignature()
    {
        //Arrange
        string payload = @"{""eventType"":""CustomerCreated""}";

        string encryptionKey = "default-secret";
        string encryptionKey2 = "custom-secret";

        //Act
        var result1 = _sut.GenerateSignature(payload, encryptionKey);
        var result2 = _sut.GenerateSignature(payload, encryptionKey2);

        //Assert
        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void GenerateSignature_StartsWithSha256AndLowerCase()
    {
        //Arrange
        string payload = @"{""eventType"":""CustomerCreated""}";
        string encryptionKey = "default-secret";

        //Act
        var signatureResult = _sut.GenerateSignature(payload, encryptionKey);

        //Assert
        Assert.StartsWith("sha256=", signatureResult);
        //Assert.Matches("^[0-9a-f]{64}$", signatureResult);
        Assert.Matches(new System.Text.RegularExpressions.Regex("^[0-9a-f]{64}$"), signatureResult["sha256=".Length..]);
    }

    [Fact]
    public void IsTimeStampValid_CurrentTimestamp_ReturnsTrue()
    {
        //Arrange
        var webhokTimestamp = DateTimeOffset.UtcNow;

        //Act
        var result = _sut.IsTimeStampValid(webhokTimestamp);

        //Assert
        Assert.True(result);
    }

    [Fact]
    public void IsTimeStampValid_TimestampWithinDefaultTolerance_ReturnsTrue()
    {
        //Arrange
        var webhookTimestamp = DateTimeOffset.UtcNow.AddMinutes(-3);

        //Act
        var result = _sut.IsTimeStampValid(webhookTimestamp);

        //Assert
        Assert.True(result);
    }

    [Fact]
    public void IsTimeStampValid_TimestampExceedDefaultTolerance_ReturnsFalse()
    {
        //Arrange
        var webhookTimestamp = DateTimeOffset.UtcNow.AddMinutes(-6);

        //Act
        var result = _sut.IsTimeStampValid(webhookTimestamp);

        //Assert
        Assert.False(result);
    }

    [Fact]
    public void IsTimeStampValid_FutureTimestamp_ReturnsTrue()
    {
        //Arrange
        var webhookTimestamp = DateTimeOffset.UtcNow.AddMinutes(1);

        //Act
        var result = _sut.IsTimeStampValid(webhookTimestamp);

        //Assert
        Assert.True(result);

    }

    [Fact]
    public void IsTimeStampValid_CustomExpiryTolerance_IsRespected()
    {
        //Arrange
        var webhookTimestamp = DateTimeOffset.UtcNow.AddMinutes(-5);

        //Act
        var withinToleranceWindowResult = _sut.IsTimeStampValid(webhookTimestamp, 6);
        var outsideToleranceLimit = _sut.IsTimeStampValid(webhookTimestamp, 3);

        //Assert
        Assert.True(withinToleranceWindowResult);
        Assert.False(outsideToleranceLimit);
    }

    [Fact]
    public void VerifySignature_CorrectSignature_ReturnsTrue()
    {
        //Arrange
        string payload = @"{""eventType"":""CustomerCreated""}";
        string secret = "my-secret";

        //Act
        var encryptedPayload = _sut.GenerateSignature(payload, secret);
        var isValidResult = _sut.VerifySignature(payload, encryptedPayload, secret);

        //Assert
        Assert.True(isValidResult);
    }

    [Fact]
    public void VerifySignature_TamperedPayload_ReturnsFalse()
    {
        //Arrange
        string payload = @"{""eventType"":""CustomerCreated""}";
        string tamperedPayload = @"{""eventType"":""CustomerCreated"",""hack"":true}";
        string secret = "my-secret";

        //Act
        var encryptedPayload = _sut.GenerateSignature(payload, secret);
        var isValidResult = _sut.VerifySignature(tamperedPayload, encryptedPayload, secret);

        //Assert
        Assert.False(isValidResult);
    }

    [Fact]
    public void VerifySignature_WrongSecret_ReturnFalse()
    {
        //Arrange
        string payload = @"{""eventType"":""CustomerCreated""}";
        string corectSecret = "correct-secret";
        string invalidSecret = "invalid-secret";

        //Act
        var encrypted = _sut.GenerateSignature(payload, corectSecret);
        var isValidResult = _sut.VerifySignature(payload, encrypted, invalidSecret);

        //Assert
        Assert.False(isValidResult);
    }

    [Fact]
    public void VerifySignature_SignatureWithoutPrefix_ReturnsFalse()
    {
        //Arrange
        string payload = @"{""eventType"":""CustomerCreated""}";
        string encrytionKey = "my-secret";

        //Act
        var encrypted = _sut.GenerateSignature(payload, encrytionKey);
        var isValidResult = _sut.VerifySignature(payload, encrypted.Replace("sha256=", string.Empty), encrytionKey);

        //Assert
        Assert.False(isValidResult);
    }

    [Fact]
    public void VerifySignature_AnyEmptyParameter_ThrowsException()
    {
        //Arrange
        string encryptionKey = string.Empty;
        string payload = string.Empty;
        string encryptedValue = string.Empty;

        //Act & Assert
        Assert.Throws<ArgumentException>(() => _sut.VerifySignature(payload, encryptedValue, encryptionKey));
    }

    [Fact]
    public void IsTimeStampValid_TimestampOlderThanAnHour_ReturnsFalse()
    {
        //Arrange - 62 minutes old has a minutes component of 2, which the old check accepted.
        var webhookTimestamp = DateTimeOffset.UtcNow.AddMinutes(-62);

        //Act
        var result = _sut.IsTimeStampValid(webhookTimestamp);

        //Assert
        Assert.False(result);
    }

    [Fact]
    public void GenerateSignatureWithTimestamp_MatchesHmacOfTimestampDotPayload()
    {
        //Arrange
        string payload = @"{""eventType"":""CustomerCreated""}";
        string secret = "my-secret";
        long timestamp = 1761400000;

        //Act
        var signature = _sut.GenerateSignature(payload, secret, timestamp);

        //Assert
        Assert.Equal(_sut.GenerateSignature($"{timestamp}.{payload}", secret), signature);
    }

    [Fact]
    public void GenerateSignatureWithTimestamp_DifferentTimestamp_GeneratesDifferentSignature()
    {
        //Arrange
        string payload = @"{""eventType"":""CustomerCreated""}";
        string secret = "my-secret";

        //Act
        var signature1 = _sut.GenerateSignature(payload, secret, 1761400000);
        var signature2 = _sut.GenerateSignature(payload, secret, 1761400001);

        //Assert
        Assert.NotEqual(signature1, signature2);
    }

    [Fact]
    public void VerifySignatureWithTimestamp_CorrectSignature_ReturnsTrue()
    {
        //Arrange
        string payload = @"{""eventType"":""CustomerCreated""}";
        string secret = "my-secret";
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        //Act
        var signature = _sut.GenerateSignature(payload, secret, timestamp);
        var isValidResult = _sut.VerifySignature(payload, signature, secret, timestamp);

        //Assert
        Assert.True(isValidResult);
    }

    [Fact]
    public void VerifySignatureWithTimestamp_ReplayedWithNewTimestamp_ReturnsFalse()
    {
        //Arrange
        string payload = @"{""eventType"":""CustomerCreated""}";
        string secret = "my-secret";
        long originalTimestamp = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();
        long replayTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        //Act
        var capturedSignature = _sut.GenerateSignature(payload, secret, originalTimestamp);
        var isValidResult = _sut.VerifySignature(payload, capturedSignature, secret, replayTimestamp);

        //Assert
        Assert.False(isValidResult);
    }

    [Fact]
    public void VerifySignatureWithTimestamp_TamperedPayload_ReturnsFalse()
    {
        //Arrange
        string payload = @"{""eventType"":""CustomerCreated""}";
        string tamperedPayload = @"{""eventType"":""CustomerCreated"",""hack"":true}";
        string secret = "my-secret";
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        //Act
        var signature = _sut.GenerateSignature(payload, secret, timestamp);
        var isValidResult = _sut.VerifySignature(tamperedPayload, signature, secret, timestamp);

        //Assert
        Assert.False(isValidResult);
    }
}
