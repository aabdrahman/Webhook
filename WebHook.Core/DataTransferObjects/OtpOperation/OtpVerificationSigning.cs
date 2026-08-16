namespace WebHook.Core.DataTransferObjects.OtpOperation;

public class OtpVerificationSigning
{
    public string Jti { get; set; }
    public string IssuedFor { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}