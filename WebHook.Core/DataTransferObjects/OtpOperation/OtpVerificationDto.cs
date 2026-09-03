namespace WebHook.Core.DataTransferObjects.OtpOperation;

public record class OtpVerificationDto
{
    public string SignedToken { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
