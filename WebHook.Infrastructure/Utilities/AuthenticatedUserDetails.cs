using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using WebHook.Core.Interfaces.Helpers;

namespace WebHook.Infrastructure.Utilities;

public sealed class AuthenticatedUserDetails : IAuthenticatedUserDetails
{
    private readonly IHttpContextAccessor _contextAccessor;

    public AuthenticatedUserDetails(IHttpContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
    }

    public bool isUserAuthenticated => _contextAccessor.HttpContext.User.Identity.IsAuthenticated;

    public string? firstName => _contextAccessor.HttpContext?.User.FindFirstValue("FirstName") ?? "";

    public string? lastName => _contextAccessor.HttpContext?.User.FindFirstValue("LastName") ?? "";

    public string assignedRole => _contextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Role)?.Value ?? "";

    public string emailAddress => _contextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Email)?.Value ?? "";

    public string userId => _contextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

    public string operationToken => _contextAccessor.HttpContext!.Request.Headers.TryGetValue("X-Operation-Token", out var operationTokenValue) ? operationTokenValue.FirstOrDefault()!.ToString() : "";

    public string? Origin => _contextAccessor.HttpContext!.Request.Headers.TryGetValue("Origin", out var originValue) ? originValue.FirstOrDefault()?.ToString() : "";

    public string? ClientId => _contextAccessor.HttpContext!.Request.Headers.TryGetValue("X-Client-Id", out var clientIdValue) ? clientIdValue.FirstOrDefault()?.ToString() : "";

    public string? ClientKey => _contextAccessor.HttpContext!.Request.Headers.TryGetValue("X-Client-Key", out var clientKeyValue) ? clientKeyValue.FirstOrDefault()?.ToString() : "";
}
