using System;
using System.Collections.Generic;
using System.Text;

namespace WebHook.Core.DataTransferObjects.Authentication;

public record class UserDetailsDto
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string UserEmailAddress { get; set; }
    public string UserName { get; set; }
    public string AssignedRole { get; set; }
    public DateTimeOffset? LastLoginDate { get; set; }
    public bool isActive { get; set; }
    public string? DeactivationJustification { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
}
