using System.ComponentModel.DataAnnotations;

namespace WebHook.Core.DataTransferObjects.Authentication;

/// <summary>
/// The request detials to change a user role.
/// </summary>
public class ChangeUserRoleRequestDto
{
    /// <summary>
    /// This is the eamil address to modify the role.
    /// It is a requeired field.
    /// Must pass the default email adress format
    /// </summary>
    [Required(ErrorMessage = "User Email Address is a required field.")]
    [EmailAddress(ErrorMessage = "Eamil Address is not in right format.")]
    public string UserEmailAddress { get; set; }
    /// <summary>
    /// This is the new role to be assigned to the user.
    /// This is a required field.
    /// </summary>
    [Required(ErrorMessage = "New role to assign is a required field.")]
    public string NewRoleToAssign { get; set; }
}