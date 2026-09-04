using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects.Authentication;

namespace Webhook.Web.ApplicationAuthenticationHandler;

public class UserJwtAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly ILocalStorageService _localStorageService;

    public UserJwtAuthenticationStateProvider(ILocalStorageService localStorageService)
    {
        _localStorageService = localStorageService;
        _anonymous = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
    }

    private AuthenticationState _anonymous;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var accessToken = await _localStorageService.GetItemAsync<TokenDto>("tokenDetails");

        if (accessToken is null)
        {
            NotifyAuthenticationStateChanged(Task.FromResult(_anonymous));
            return _anonymous;
        }

        var userClaims = JwtParser.ParseClaimsFromJwt(accessToken.accessToken);

        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims: userClaims, "jwtAuthType", nameType: ClaimTypes.Name, roleType: ClaimTypes.Role)));
    }

    public async Task NotifyUserLogout()
    {
        await _localStorageService.RemoveItemAsync("accessToken");
        NotifyAuthenticationStateChanged(Task.FromResult(_anonymous));
    }
}
