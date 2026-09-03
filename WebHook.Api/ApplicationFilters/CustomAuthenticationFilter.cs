using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.Net.Http.Headers;
using Serilog;
using System.Security.Claims;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.Interfaces.Helpers;

namespace WebHook.Api.ApplicationFilters;

/// <summary>
/// A custom authorization filter that validates incoming JWT Bearer tokens against
/// a distributed cache to ensure the token has not been invalidated after issuance.
/// </summary>
/// <remarks>
/// This filter supplements standard JWT signature and expiry validation by checking
/// that the token's JTI (JWT ID) matches the most recently issued JTI stored in the
/// cache for the authenticated user.
///
/// This prevents token replay attacks in scenarios where a token has been superseded
/// by a refresh operation or explicitly revoked on logout — both of which write a new
/// JTI to the cache, making any prior token immediately invalid even if it has not
/// yet expired.
///
/// Prerequisites:
/// <list type="bullet">
///   <item><description>JWT Bearer authentication middleware must be registered before this filter runs so that <see cref="HttpContext.User"/> is populated.</description></item>
///   <item><description>The cache must be seeded with the user's current JTI at login and updated on every token refresh.</description></item>
///   <item><description>The cache must be cleared for the user's email key on logout.</description></item>
/// </list>
///
/// This filter respects <see cref="IAllowAnonymous"/> — endpoints decorated with
/// <c>[AllowAnonymous]</c> are not subject to this validation.
/// </remarks>
/// <param name="cacheService">
/// The cache service used to retrieve the most recently issued JTI for a given user.
/// </param>
/// <param name="logger">
/// Logger for recording authorization failures and security events.
/// </param>
public class CustomAuthenticationFilter(ICacheService cacheService, ILogger<CustomAuthenticationFilter> logger) : IAsyncAuthorizationFilter
{
    /// <summary>
    /// Executes the authorization logic for the current request.
    /// </summary>
    /// <remarks>
    /// Validation steps performed in order:
    /// <list type="number">
    ///   <item><description>Skip validation if the endpoint allows anonymous access.</description></item>
    ///   <item><description>Verify the Authorization header is present.</description></item>
    ///   <item><description>Verify the header value starts with the "Bearer " scheme prefix.</description></item>
    ///   <item><description>Verify the token value after stripping the prefix is not empty.</description></item>
    ///   <item><description>Extract the JTI claim from the authenticated user principal.</description></item>
    ///   <item><description>Extract the email claim from the authenticated user principal.</description></item>
    ///   <item><description>Retrieve the cached JTI for the user's email and compare it against the token JTI.</description></item>
    /// </list>
    /// Any failure at any step returns <c>401 Unauthorized</c> with a generic message
    /// to avoid leaking information about the specific failure reason to the caller.
    /// </remarks>
    /// <param name="context">The authorization filter context for the current request.</param>
    public virtual async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Step 1 — respect [AllowAnonymous]
        bool hasAllowAnonymous = context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any();

        if (hasAllowAnonymous) return;

        // Step 2 — Authorization header must be present
        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderNames.Authorization, out var authorizationBearerHeader))
        {
            logger.LogWarning("Request rejected — Authorization header missing. Path: {Path}", context.HttpContext.Request.Path);
            Reject(context);
            return;
        }

        // Step 3 — Must use Bearer scheme
        string requestAuthToken = authorizationBearerHeader.ToString();
        if (string.IsNullOrWhiteSpace(requestAuthToken) || !requestAuthToken.StartsWith("Bearer "))
        {
            logger.LogWarning("Request rejected — Authorization header does not use Bearer scheme.");
            Reject(context);
            return;
        }

        // Step 4 — Token value must not be empty after stripping the scheme prefix
        string requestToken = requestAuthToken["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(requestToken))
        {
            logger.LogWarning("Request rejected — Bearer token value is empty.");
            Reject(context);
            return;
        }

        // Step 5 — JTI claim must be present and parseable as a Guid
        string? tokenStringJti = context.HttpContext.User.FindFirstValue(JwtRegisteredClaimNames.Jti);

        if (string.IsNullOrWhiteSpace(tokenStringJti) || !Guid.TryParse(tokenStringJti, out Guid tokenJti))
        {
            logger.LogWarning("Request rejected — JTI claim missing or invalid in token.");
            Reject(context);
            return;
        }

        // Step 6 — Email claim must be present
        string? userEmail = context.HttpContext.User
            .FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            logger.LogWarning("Request rejected — Email claim missing in token.");
            Reject(context);
            return;
        }

        // Step 7 — Cached JTI must exist and match the token JTI
        Guid cachedJti = await cacheService.GetItemsFromCacheAsync<Guid>(userEmail);

        if (cachedJti == default)
        {
            logger.LogWarning("Request rejected — no cached JTI for user {Email}. Session may have expired or been revoked.", userEmail);
            Reject(context);
            return;
        }

        if (cachedJti != tokenJti)
        {
            logger.LogWarning("Request rejected — JTI mismatch for user {Email}. " + "Token JTI: {TokenJti}, Cached JTI: {CachedJti}. Possible token replay attempt.", userEmail, tokenJti, cachedJti);
            Reject(context);
            return;
        }
    }

    /// <summary>
    /// Sets a generic 401 Unauthorized result on the filter context.
    /// A generic message is used intentionally to avoid leaking information
    /// about the specific reason for rejection to the caller.
    /// </summary>
    private static void Reject(AuthorizationFilterContext context)
    {
        context.Result = new UnauthorizedObjectResult(GenericResponse<string>.Failure(null, "Unauthorized Access.", System.Net.HttpStatusCode.Unauthorized));
    }
}
