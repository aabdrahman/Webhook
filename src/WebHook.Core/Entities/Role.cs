

using Microsoft.AspNetCore.Identity;

namespace WebHook.Core.Entities;

/// <summary>
/// An ASP.NET Core Identity role (for example <c>USER</c> or <c>Admin</c>) with audit fields.
/// </summary>
public class Role : IdentityRole<Guid>
{
    /// <summary>When the role was created.</summary>
    public DateTimeOffset? CreatedAt { get; set; }
    /// <summary>What the role is for.</summary>
    public string Description { get; set; }
    /// <summary>Whether the role can be assigned.</summary>
    public bool IsActive { get; set; } = true;
}