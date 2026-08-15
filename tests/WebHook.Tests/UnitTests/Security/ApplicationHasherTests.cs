using System;
using System.Collections.Generic;
using System.Text;
using WebHook.Infrastructure.Security;

namespace WebHook.Tests.UnitTests.Security;

public class ApplicationHasherTests
{
    private ApplicationHasher CreateSut() => new ApplicationHasher();

    [Fact]
    public async Task HashSecret_ValidSecret_ReturnsNonEmptyHash()
    {
        //Arrange
        var sut = CreateSut();
        var secret = "ABCD12345";

        //Act
        var hashResult = await sut.HashSecret(secret);

        //Assert
        Assert.NotNull(hashResult);
        Assert.Contains("-", hashResult);
    }

    [Fact]
    public async Task HashSecret_SameSecret_GeneratesDifferentHashes()
    {
        //Arrange
        var sut = CreateSut();
        var secret = "ABCD12345";

        //Act
        var hashResult1 = await sut.HashSecret(secret);
        var hashResult2 = await sut.HashSecret(secret);

        //Assert
        Assert.NotNull(hashResult1);
        Assert.NotNull(hashResult2);
        Assert.NotEqual(hashResult1, hashResult2);
    }

    [Fact]
    public async Task HashSecret_ValidSecret_ReturnsHashContainingHashAndSalt()
    {
        //Arrange
        var sut = CreateSut();
        var secret = "ABCD12345";

        //Act
        var hashResult = await sut.HashSecret(secret);

        //Assert
        Assert.NotNull(hashResult);
        Assert.Contains("-", hashResult);
        Assert.Equal(2, hashResult.Split("-").Length);
    }

    [Fact]
    public async Task ValidateHashedSecret_ValidSecret_ReturnsTrue()
    {
        //Arrange
        var sut = CreateSut();
        string secret = "ABCD12345";
        var hashedSecret = await sut.HashSecret(secret);
        Assert.NotNull(hashedSecret);

        //Act
        var result = await sut.ValidateHashedSecret(hashedSecret, secret);

        //Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateHashedSecret_InvalidSecret_ReturnsFalse()
    {
        //Arrange
        var sut = CreateSut();
        string secret = "ABCD12345";
        var hashedSecret = await sut.HashSecret(secret);
        Assert.NotNull(hashedSecret);

        //Act
        var result = await sut.ValidateHashedSecret(hashedSecret, "ABCDE123456");

        //Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateHashedSecret_ModifiedHash_ReturnsFalse()
    {
        //Arrange
        var sut = CreateSut();
        string secret = "ABCD12345";
        var hashedSecret = await sut.HashSecret(secret);
        Assert.NotNull(hashedSecret);

        //Act
        var result = await sut.ValidateHashedSecret(string.Concat(hashedSecret, "234"), secret);

        //Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateHashedSecret_ModifiedSalt_ReturnsFalse()
    {
        //Arrange
        var sut = CreateSut();
        string secret = "ABCD12345";
        var hashedSecret = await sut.HashSecret(secret);
        Assert.NotNull(hashedSecret);

        //Act
        var result = await sut.ValidateHashedSecret(string.Concat("2teg2i3eu", hashedSecret), secret);

        //Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateHashedSecret_EmptyHash_ReturnsFalse()
    {
        //Arrange
        var sut = CreateSut();

        //Act
        var result = await sut.ValidateHashedSecret(string.Empty, "ABCD12345");

        //Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("hash-only")]
    [InlineData("hash-salt-extra")]
    [InlineData("-salt")]
    [InlineData("hash-")]
    public async Task ValidateHashedSecret_MalformedHash_ReturnsFalse(
    string malformedHash)
    {
        var sut = CreateSut();
        // Act
        var result = await sut.ValidateHashedSecret(
            malformedHash,
            "583214");

        // Assert
        Assert.False(result);
    }
}
