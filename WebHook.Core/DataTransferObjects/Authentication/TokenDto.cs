namespace WebHook.Core.DataTransferObjects.Authentication;

public record class TokenDto
(string accessToken, string refreshToken);
