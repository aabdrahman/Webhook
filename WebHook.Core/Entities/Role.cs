

using Microsoft.AspNetCore.Identity;

namespace WebHook.Core.Entities;

public class Role : IdentityRole<Guid>
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Description { get; set; }
    public bool IsActive { get; set; } = true;
}