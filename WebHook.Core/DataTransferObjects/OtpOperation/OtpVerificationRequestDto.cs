namespace WebHook.Core.DataTransferObjects.OtpOperation;

public class OtpVerificationRequestDto
{
    public string Otp { get; set; }
    public string EmailAddress { get; set; }
}
