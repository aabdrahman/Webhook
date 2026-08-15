using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace WebHook.Core.DataTransferObjects.Authentication;

/// <summary>
/// Data Transfer Object for changing a user's password.
/// </summary>
public record class ChangePasswordDto
{
    /// <summary>
    /// The username or email address identifying the user account to change password.
    /// </summary>
    [Required(ErrorMessage = "Kindly provide the username or email address.")]
    public string UserNameOrEmailAddress { get; set; }

    /// <summary>
    /// Gets or sets the user's current password.
    /// </summary>
    [Required(ErrorMessage = "Kindly provide your old password.")]
    public string OldPassword { get; set; }

    /// <summary>
    /// Gets or sets the new password. Must meet complexity requirements.
    /// </summary>
    [Required(ErrorMessage = "Kindly provide your new password.")]
    [RegularExpression(
        "^(?=.*\\d)(?=.*[a-z])(?=.*[A-Z])(?=.*[^a-zA-Z0-9]).{10,}$",
        ErrorMessage = "Password must be at least 10 characters long and contain at least one digit, lowercase letter, uppercase letter, and special character.")]
    public string NewPassword { get; set; }

    /// <summary>
    /// Gets or sets the confirmation of the new password. Must match NewPassword.
    /// </summary>
    [Required(ErrorMessage = "Kindly confirm your new password.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Password confirmation does not match.")]
    public string ConfirmNewPassword { get; set; }
}
