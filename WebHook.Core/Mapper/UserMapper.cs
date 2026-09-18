using System.Linq.Expressions;
using WebHook.Core.DataTransferObjects.Authentication;
using WebHook.Core.Entities;

namespace WebHook.Core.Mapper;

public static class UserMapper
{

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
