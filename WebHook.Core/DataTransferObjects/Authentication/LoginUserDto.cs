using System.ComponentModel.DataAnnotations;

namespace WebHook.Core.DataTransferObjects.Authentication;

/// <summary>
/// Represents the credentials required to authenticate a user.
/// The user can provide either their username or email address,
/// together with their password.
/// </summary>
public record class LoginUserDto
{
    /// <summary>
    /// Gets or sets the user's username or email address.
    /// The value is treated as an email address when it contains the '@' character;
    /// otherwise, it is treated as a username.
    /// </summary>
    [Required(ErrorMessage = "Kindly enter either your username or email address.")]
    public string UserNameOrEmailAddress { get; set; }

    /// <summary>
    /// Gets or sets the user's password.
    /// The password must contain at least one uppercase letter, one lowercase letter,
    /// one digit, and one non-alphanumeric character, and must be at least 10 characters long.
    /// </summary>
    [Required(ErrorMessage = "Password is a required field.")]
    [RegularExpression(
        "^(?=.*\\d)(?=.*[a-z])(?=.*[A-Z])(?=.*[^a-zA-Z0-9]).{10,}$",
        ErrorMessage = "Password must contain at least one digit, lowercase letter, uppercase letter, and non-alphanumeric character.")]
    public string Password { get; set; }
}
