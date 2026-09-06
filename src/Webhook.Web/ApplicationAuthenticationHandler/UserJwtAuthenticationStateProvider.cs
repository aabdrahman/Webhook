using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using System.Text.Json;
using Webhook.Web.Handlers.Authentication;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects.Authentication;

namespace Webhook.Web.ApplicationAuthenticationHandler;

public class UserJwtAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly ILocalStorageService _localStorageService;
    private readonly RefreshTokenHandler _refreshTokenHandler;

    public UserJwtAuthenticationStateProvider(ILocalStorageService localStorageService, RefreshTokenHandler refreshTokenHandler)
    {
        _localStorageService = localStorageService;
        _refreshTokenHandler = refreshTokenHandler;
        _anonymous = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
    }

    private AuthenticationState _anonymous;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        //Get access token from local storage
        var accessToken = await _localStorageService.GetItemAsync<TokenDto>(ApplicationClientHelper.StorageKey);
        Console.WriteLine($"Token fetched from store successfully - {accessToken?.accessToken}");
        //check if access token is null, if null return anonymous user
        if (accessToken is null)
        {
            Console.WriteLine("Token is empty from store.");
            NotifyAuthenticationStateChanged(Task.FromResult(_anonymous));
            return _anonymous;
        }

        //Extract claims from access token and check if the token is expired, if expired return anonymous user
        var userClaims = JwtParser.ParseClaimsFromJwt(accessToken.accessToken);
        Console.WriteLine($"Claims fetched from token successfully - {JsonSerializer.Serialize(userClaims)}");
        var expiry = userClaims.FirstOrDefault(x => x.Type.Equals("exp", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
        if(string.IsNullOrEmpty(expiry) || !long.TryParse(expiry, out long expiryTime))
        {
            Console.WriteLine($"Token exp value could not be parsed from claims - {expiry}");
            await _localStorageService.RemoveItemAsync(ApplicationClientHelper.StorageKey);
            NotifyAuthenticationStateChanged(Task.FromResult(_anonymous));
            return _anonymous;
        }

        //Convert the exp value to a valid datetime
        DateTimeOffset expTime = DateTimeOffset.FromUnixTimeSeconds(expiryTime);

        if (DateTimeOffset.UtcNow >= expTime)
        {
            Console.WriteLine($"Token already expired at - {expTime}");
            //The expiry time has exceeded. Token already expired. Remove expired token and set anonymous
            await _localStorageService.RemoveItemAsync(ApplicationClientHelper.StorageKey);
            NotifyAuthenticationStateChanged(Task.FromResult(_anonymous));
            return _anonymous;
        }

        var timeToExpiry = (expTime - DateTimeOffset.UtcNow).TotalSeconds;
        if(timeToExpiry <= ApplicationClientHelper.SessionRefreshWindow)
        {
            try
            {
                Console.WriteLine("Begin refresh operation -");
                string? refreshTokenResult = await _refreshTokenHandler.RefreshTokenAsync(accessToken);

                if (string.IsNullOrEmpty(refreshTokenResult))
                {
                    Console.WriteLine("Refresh token failed....");
                    //The token could not be refreshed successfully, so we remove any possible stale ones and return accordingly
                    await _localStorageService.RemoveItemAsync(ApplicationClientHelper.StorageKey);
                    NotifyAuthenticationStateChanged(Task.FromResult(_anonymous));
                    return _anonymous;
                }

                userClaims = JwtParser.ParseClaimsFromJwt(refreshTokenResult);
                if(userClaims is null)
                {
                    Console.WriteLine($"New token parsed successfully - {JsonSerializer.Serialize(userClaims)}");
                   //User claims is null, hence, we could not refresh successfuly.
                    await _localStorageService.RemoveItemAsync(ApplicationClientHelper.StorageKey);
                    NotifyAuthenticationStateChanged(Task.FromResult(_anonymous));
                    return _anonymous;
                }
            }
            catch (Exception ex)
            {
                //In case an exception happens when refreshing token, we remove the token from the storage and then set identity as anonymous
                await _localStorageService.RemoveItemAsync(ApplicationClientHelper.StorageKey);
                NotifyAuthenticationStateChanged(Task.FromResult(_anonymous));
                return _anonymous;
            }
        }

        Console.WriteLine("User session validated successfully....");
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims: userClaims, "jwtAuthType", nameType: ClaimTypes.Name, roleType: ClaimTypes.Role)));
    }

    public async Task NotifyUserLogout()
    {
        await _localStorageService.RemoveItemAsync(ApplicationClientHelper.StorageKey);
        NotifyAuthenticationStateChanged(Task.FromResult(_anonymous));
    }

    public async Task NotifyUserLogin()
    {
        var token = await _localStorageService.GetItemAsync<TokenDto>(ApplicationClientHelper.StorageKey);

        var claims = JwtParser.ParseClaimsFromJwt(token?.accessToken ?? "");
        var identity = new ClaimsIdentity(claims, "jwtAuthType", ClaimTypes.Name, ClaimTypes.Role);

        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity))));
    }
}
