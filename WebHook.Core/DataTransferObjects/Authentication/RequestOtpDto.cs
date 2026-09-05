using System.ComponentModel.DataAnnotations;
using WebHook.Core.Constants;

namespace WebHook.Core.DataTransferObjects.Authentication;

public record class RequestOtpDto
{
    [Required(ErrorMessage = "Kindly provide a valid user name or email address.")]
    public string UserNameOrEmailAddress { get; set; }
    [Required(ErrorMessage = "Kindly provide otp purpose.")]
    public OtpPurpose Purpose { get; set; }
}
