using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using WebHook.Core.Interfaces.Helpers;

namespace WebHook.Infrastructure.Utilities;

/// <summary>
/// <see cref="IAuthenticatedUserDetails"/> implementation that reads the current
/// <see cref="HttpContext"/> through <see cref="IHttpContextAccessor"/>.
/// </summary>
public sealed class AuthenticatedUserDetails : IAuthenticatedUserDetails
{
    private readonly IHttpContextAccessor _contextAccessor;

    /// <summary>
    /// Initialises a new instance of the <see cref="AuthenticatedUserDetails"/> class.
    /// </summary>
    /// <param name="contextAccessor">Accessor for the current HTTP context.</param>
    public AuthenticatedUserDetails(IHttpContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
    }

    /// <inheritdoc />
    public bool isUserAuthenticated => _contextAccessor.HttpContext.User.Identity.IsAuthenticated;

    /// <inheritdoc />
    public string? firstName => _contextAccessor.HttpContext?.User.FindFirstValue("FirstName") ?? "";

    /// <inheritdoc />
    public string? lastName => _contextAccessor.HttpContext?.User.FindFirstValue("LastName") ?? "";

    /// <inheritdoc />
    public string assignedRole => _contextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Role)?.Value ?? "";

    /// <inheritdoc />
    public string emailAddress => _contextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Email)?.Value ?? "";

    /// <inheritdoc />
    public string userId => _contextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

    /// <inheritdoc />
    public string operationToken => _contextAccessor.HttpContext!.Request.Headers.TryGetValue("X-Operation-Token", out var operationTokenValue) ? operationTokenValue.FirstOrDefault()!.ToString() : "";

    /// <inheritdoc />
    public string? Origin => _contextAccessor.HttpContext!.Request.Headers.TryGetValue("Origin", out var originValue) ? originValue.FirstOrDefault()?.ToString() : "";

    /// <inheritdoc />
    public string? ClientId => _contextAccessor.HttpContext!.Request.Headers.TryGetValue("X-Client-Id", out var clientIdValue) ? clientIdValue.FirstOrDefault()?.ToString() : "";

    /// <inheritdoc />
    public string? ClientKey => _contextAccessor.HttpContext!.Request.Headers.TryGetValue("X-Client-Key", out var clientKeyValue) ? clientKeyValue.FirstOrDefault()?.ToString() : "";
}
