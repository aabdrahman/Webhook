using System;
using System.Collections.Generic;
using System.Text;
using WebHook.Infrastructure.Utilities;

namespace WebHook.Tests.UnitTests.Helpers;

public class OtpGeneratorTests
{

    private OtpGenerator CreateSut() => new OtpGenerator();

    [Fact]
    public void GenerateOtp_DefaultParameters_ReturnsProperLength()
    {
        //Arrange
        var sut = CreateSut();

        //Act
        var result = sut.GenerateOtp();

        //Assert
        Assert.NotNull(result);
        Assert.Equal(6, result.Length);
        Assert.All(result, character =>
        {
            Assert.True(char.IsDigit(character));
        });
    }

    [Fact]
    public void GenerateOtp_ConfiguredLegth_ReturnsProperLength()
    {
        //Arrange
        var sut = CreateSut();
        int otpLegth = 8;

        //Act
        var result = sut.GenerateOtp(length: otpLegth);

        //Assert
        Assert.NotNull(result);
        Assert.Equal(otpLegth, result.Length);
        Assert.All(result, character =>
        {
            Assert.True(char.IsDigit(character));
        });
    }

    [Fact]
    public void GenerateOtp_LengthAsZero_ReturnsEmptyString()
    {
        //Arrange
        var sut = CreateSut();

        //Act
        var result = sut.GenerateOtp(0);

        //Assert
        Assert.NotNull(result);
        Assert.True(string.IsNullOrEmpty(result), $"OTP generated - {result}");
    }

    [Fact]
    public void GenerateOtp_LengthLessThanZero_ReturnsEmptyString()
    {
        //Arrange
        var sut = CreateSut();

        //Act
        var result = sut.GenerateOtp(-1);

        //Assert
        Assert.NotNull(result);
        Assert.True(string.IsNullOrEmpty(result), $"OTP generated - {result}");
    }

    [Fact]
    public void GenerateOtp_LengthExceedsMaxValue_ReturnsEmptyString()
    {
        //Arrange
        var sut = CreateSut();

        //Act
        var result = sut.GenerateOtp(6, 4);

        //Assert
        Assert.NotNull(result);
        Assert.True(string.IsNullOrEmpty(result), $"OTP generated - {result}");
    }
}
