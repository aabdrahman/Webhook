namespace WebHook.Core.Interfaces.Helpers;

public interface IOtpGenerator
{
    string GenerateOtp(int length = 6, int maxLength = 12);
}
