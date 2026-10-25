namespace WebHook.Core.Interfaces.Helpers;

/// <summary>
/// Exposes the caller of the current HTTP request: claims from the authenticated
/// user's JWT, and the identity headers sent by service clients.
/// </summary>
/// <remarks>
/// Claim-based members return an empty string when the claim is absent.
/// </remarks>
public interface IAuthenticatedUserDetails
{
    /// <summary>Whether the request carries an authenticated user.</summary>
    bool isUserAuthenticated { get; }
    /// <summary>The user's first name, from the <c>FirstName</c> claim.</summary>
    string? firstName { get; }
    /// <summary>The user's last name, from the <c>LastName</c> claim.</summary>
    string? lastName { get; }
    /// <summary>The user's role, from the role claim.</summary>
    string assignedRole { get; }
    /// <summary>The user's email address, from the email claim.</summary>
    string emailAddress { get; }
    /// <summary>The user's ID, from the name identifier claim.</summary>
    string userId { get; }
    /// <summary>The OTP operation token from the <c>X-Operation-Token</c> header.</summary>
    string operationToken { get; }
    /// <summary>The value of the <c>Origin</c> header.</summary>
    string? Origin { get; }
    /// <summary>The service client ID from the <c>X-Client-Id</c> header.</summary>
    string? ClientId { get; }
    /// <summary>The raw service client key from the <c>X-Client-Key</c> header.</summary>
    string? ClientKey { get; }
}
