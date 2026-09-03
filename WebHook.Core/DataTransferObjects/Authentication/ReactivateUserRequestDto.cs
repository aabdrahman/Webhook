using System.ComponentModel.DataAnnotations;

namespace WebHook.Core.DataTransferObjects.Authentication;

/// <summary>
/// This is the required details for reactivating a user profile.
/// </summary>
public record class ReactivateUserRequestDto
{
    /// <summary>
    /// The username or email address identifying the user account to reactivate.
    /// </summary>
    [Required(ErrorMessage = "Kindly provide the username or email address.")]
    public string UserNameOrEmailAddress { get; set; }
}