using System.ComponentModel.DataAnnotations;
using WebHook.Core.Constants;

namespace WebHook.Core.DataTransferObjects.Authentication;

/// <summary>
/// Request to send a one-time password to a user's registered email address.
/// </summary>
public record class RequestOtpDto
{
    /// <summary>
    /// The user name or email address of the account the OTP is requested for.
    /// </summary>
    [Required(ErrorMessage = "Kindly provide a valid user name or email address.")]
    public string UserNameOrEmailAddress { get; set; }
    /// <summary>
    /// The operation the OTP will authorise once verified.
    /// </summary>
    [Required(ErrorMessage = "Kindly provide otp purpose.")]
    public OtpPurpose Purpose { get; set; }
}
