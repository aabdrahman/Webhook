using WebHook.Core.Constants;

namespace WebHook.Core.DataTransferObjects.Authentication;

public record class RequestOtpDto
{
    public string UserNameOrEmailAddress { get; set; }
    public OtpPurpose Purpose { get; set; }
}
