using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;

namespace WebHook.Core.Interfaces.Services;

public interface IUserService
{
    Task<GenericResponse<string>> CreateUserAsync(CreateUserDto createUser, CancellationToken ct = default);
}
