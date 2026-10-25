using System.Linq.Expressions;
using WebHook.Core.DataTransferObjects.Authentication;
using WebHook.Core.Entities;

namespace WebHook.Core.Mapper;

/// <summary>
/// Maps between <see cref="User"/> entities and their DTOs.
/// </summary>
public static class UserMapper
{

    /// <summary>
    /// Creates an active <see cref="User"/> from a registration request. The password is not mapped;
    /// it is set by ASP.NET Core Identity when the user is created.
    /// </summary>
    /// <param name="createUserDto">The registration request.</param>
    /// <returns>A new, unsaved user.</returns>
    public static User ToEntity(this CreateUserDto createUserDto)
    {
        return new User()
        {
            FirstName = createUserDto.FirstName,
            LastName = createUserDto.LastName,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            Email = createUserDto.EmailAddress,
            UserName = createUserDto.UserName
        };
    }

    /// <summary>
    /// A LINQ projection from <see cref="User"/> to <see cref="UserDetailsDto"/> for EF Core queries.
    /// </summary>
    /// <remarks><see cref="UserDetailsDto.AssignedRole"/> is left empty because roles live in a separate table.</remarks>
    /// <returns>The projection expression.</returns>
    public static Expression<Func<User, UserDetailsDto>> ToUserDetailsExpression()
    {
        return user => new UserDetailsDto()
        {
            Id = user.Id,
            UserName = user.UserName,
            UserEmailAddress = user.NormalizedEmail,
            FirstName = user.FirstName,
            LastName= user.LastName,
            isActive = user.IsActive,
            LastLoginDate = user.LastLoginDate,
            AssignedRole = string.Empty,
            CreatedAt = user.CreatedAt,
            DeactivationJustification = user.DeactivationJustification,
            LockoutEnd = user.LockoutEnd
        };
    }
}
