namespace WebHook.Core.DataTransferObjects.Authentication;

/// <summary>
/// A JWT access token and its paired refresh token, returned on login and token refresh.
/// </summary>
/// <param name="accessToken">The signed JWT used to authenticate API requests.</param>
/// <param name="refreshToken">The opaque token used to obtain a new access token once it expires.</param>
public record class TokenDto
(string accessToken, string refreshToken);
