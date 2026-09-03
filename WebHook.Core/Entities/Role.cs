

using Microsoft.AspNetCore.Identity;

namespace WebHook.Core.Entities;

public class Role : IdentityRole<Guid>
{
    public DateTimeOffset? CreatedAt { get; set; }
    public string Description { get; set; }
    public bool IsActive { get; set; } = true;
}