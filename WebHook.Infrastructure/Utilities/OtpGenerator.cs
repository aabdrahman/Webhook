using Serilog;
using System.Security.Cryptography;
using WebHook.Core.Interfaces.Helpers;

namespace WebHook.Infrastructure.Utilities;

//public class OtpGenerator : IOtpGenerator
//{

//    public async Task<string> GenerateOtpAsync(int length = 6)
//    {
//        var logger = Serilog.Log.ForContext("ClassName", nameof(OtpGenerator)).ForContext("MethodName", nameof(GenerateOtpAsync));
//        try
//        {
//            await Task.Delay(1);
//            var rngBytes = new byte[length];
//            using (var rndGen = RandomNumberGenerator.Create())
//            {
//                rndGen.GetBytes(rngBytes);
//            }

//            uint integerValue = BitConverter.ToUInt32(rngBytes, 0);

//            return (integerValue % Math.Pow(10, length)).ToString().PadLeft(length, '0');
//        }
//        catch (Exception ex)
//        {
//            logger.Error(ex, "An error occurred while geenrating OTP.");
//            return "";
//        }
//    }
//}

public sealed class OtpGenerator : IOtpGenerator
{
    private const string ClassName = nameof(OtpGenerator);

    public string GenerateOtp(int length = 6, int maxLength = 12)
    {
        ILogger logger = Log.ForContext("ClassName", ClassName).ForContext("MethodName", nameof(GenerateOtp));

        try
        {
            if (length <= 0)
            {
                //throw new ArgumentOutOfRangeException(nameof(length), "OTP length must be greater than zero.");
                logger.Warning("OTP legth must be greater than zero. Provided Value is invalid - {0}", length);
                return string.Empty;
            }

            if (length > maxLength)
            {
                logger.Warning("The preovided value: {0} exceeds the maximum otp length: {1}.", length, maxLength);
                //throw new ArgumentOutOfRangeException(nameof(length), "OTP length cannot exceed 12 digits.");
                return string.Empty;
            }

            var otpCharacters = new char[length];

            for (var i = 0; i < length; i++)
            {
                otpCharacters[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
            }

            return new string(otpCharacters);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error occurred while generating OTP.");
            return string.Empty;
        }
    }

}