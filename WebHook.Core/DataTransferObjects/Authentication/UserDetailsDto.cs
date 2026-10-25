using System;
using System.Collections.Generic;
using System.Text;

namespace WebHook.Core.DataTransferObjects.Authentication;

/// <summary>
/// Read model describing a user account and its current status.
/// </summary>
public record class UserDetailsDto
{
    /// <summary>The unique identifier of the user.</summary>
    public Guid Id { get; set; }
    /// <summary>When the account was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>The user's first name.</summary>
    public string FirstName { get; set; }
    /// <summary>The user's last name.</summary>
    public string LastName { get; set; }
    /// <summary>The user's email address.</summary>
    public string UserEmailAddress { get; set; }
    /// <summary>The user's login name.</summary>
    public string UserName { get; set; }
    /// <summary>The name of the role assigned to the user (for example <c>USER</c> or <c>Admin</c>).</summary>
    public string AssignedRole { get; set; }
    /// <summary>When the user last logged in, or <see langword="null"/> if they never have.</summary>
    public DateTimeOffset? LastLoginDate { get; set; }
    /// <summary>Whether the account is active. Deactivated users cannot log in.</summary>
    public bool isActive { get; set; }
    /// <summary>The reason recorded when the account was deactivated, if any.</summary>
    public string? DeactivationJustification { get; set; }
    /// <summary>When the current lockout ends, or <see langword="null"/> if the account is not locked out.</summary>
    public DateTimeOffset? LockoutEnd { get; set; }
}
