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
}
