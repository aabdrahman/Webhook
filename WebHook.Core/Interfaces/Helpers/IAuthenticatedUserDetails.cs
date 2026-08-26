namespace WebHook.Core.Interfaces.Helpers;

public interface IAuthenticatedUserDetails
{
    bool isUserAuthenticated { get; }
    string? firstName { get; }
    string? lastName { get; }
    string assignedRole { get; }
    string emailAddress { get; }
    string userId { get; }
    string operationToken { get; }
    string? Origin { get; }
}
