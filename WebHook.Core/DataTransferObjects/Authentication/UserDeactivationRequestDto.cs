using System.ComponentModel.DataAnnotations;

namespace WebHook.Core.DataTransferObjects.Authentication;


/// <summary>
/// This is the required details for deactivating a user profile.
/// </summary>
public record class UserDeactivationRequestDto
{
    /// <summary>
    /// The username or email address identifying the user account to deactivate.
    /// </summary>
    [Required(ErrorMessage = "Kindly provide the username or email address.")]
    public string UserNameOrEmailAddress { get; set; }

    /// <summary>
    /// The business justification for deactivating the user account.
    /// </summary>
    [Required(ErrorMessage = "A deactivation justification is required.")]
    [StringLength(500, MinimumLength = 10, ErrorMessage = "The deactivation justification must be between 10 and 500 characters.")]
    public string DeactivationJustification { get; set; }

}
