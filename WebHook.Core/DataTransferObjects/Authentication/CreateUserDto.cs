using System.ComponentModel.DataAnnotations;

namespace WebHook.Core.DataTransferObjects.Authentication;

/// <summary>
/// Represents the data required to create a new user account.
/// </summary>
public record class CreateUserDto
{
    /// <summary>
    /// Gets or sets the user's first name.
    /// </summary>
    [Required(ErrorMessage = "First Name is a required field.")]
    [StringLength(50, ErrorMessage = "First Name cannot exceed 50 characters.")]
    public string FirstName { get; set; }

    /// <summary>
    /// Gets or sets the user's last name.
    /// </summary>
    [Required(ErrorMessage = "Last Name is a required field.")]
    [StringLength(50, ErrorMessage = "Last Name cannot exceed 50 characters.")]
    public string LastName { get; set; }

    /// <summary>
    /// Gets or sets the user's email address.
    /// </summary>
    [Required(ErrorMessage = "Email is a required field.")]
    [EmailAddress(ErrorMessage = "The provided email is not a valid email address.")]
    public string EmailAddress { get; set; }

    /// <summary>
    /// Gets or sets the password for the new user account.
    /// The password must contain at least one digit, one lowercase letter,
    /// one uppercase letter, and one non-alphanumeric character.
    /// </summary>
    [Required(ErrorMessage = "Password is a required field.")]
    [RegularExpression(
        "^(?=.*\\d)(?=.*[a-z])(?=.*[A-Z])(?=.*[^a-zA-Z0-9]).{10,}$",
        ErrorMessage = "Password must contain at least one digit, lowercase letter, uppercase letter, and non-alphanumeric character.")]
    public string Password { get; set; }

    /// <summary>
    /// Gets or sets the password confirmation.
    /// The value must match the <see cref="Password"/> property.
    /// </summary>
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; }

    /// <summary>
    /// Gets or sets the unique username for the new user account.
    /// Also, the username canot contain: "@"
    /// </summary>
    [Required(ErrorMessage = "User name is required.")]
    [RegularExpression(@"^[^@]+$", ErrorMessage = "Username cannot contain '@'.")]
    public string UserName { get; set; }
}
