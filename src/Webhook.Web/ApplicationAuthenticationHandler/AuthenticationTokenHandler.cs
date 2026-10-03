using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using System.Net;
using System.Net.Http.Headers;
using Webhook.Web.Handlers.Authentication;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects.Authentication;

namespace Webhook.Web.ApplicationAuthenticationHandler;

public class AuthenticationTokenHandler : DelegatingHandler
{
    private readonly ILocalStorageService _localStorageService;
    private readonly RefreshTokenHandler _refreshTokenHandler;
    private readonly AuthenticationStateProvider _authenticationStateProvider;

    public AuthenticationTokenHandler(ILocalStorageService localStorageService, RefreshTokenHandler refreshTokenHandler, AuthenticationStateProvider authenticationStateProvider)
    {
        _localStorageService = localStorageService;
        _refreshTokenHandler = refreshTokenHandler;
        _authenticationStateProvider = authenticationStateProvider;
    }

    override protected async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var accessToken = await _localStorageService.GetItemAsync<TokenDto>(ApplicationClientHelper.StorageKey, cancellationToken);

        if(accessToken is null)
        {
            await ((UserJwtAuthenticationStateProvider)_authenticationStateProvider).NotifyUserLogout();
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue(ApplicationClientHelper.ApplicationBearerHeader, accessToken?.accessToken);

        var result = await base.SendAsync(request, cancellationToken);

        if(result.StatusCode == HttpStatusCode.Unauthorized)
        {
            var newAccessToken = await _refreshTokenHandler.RefreshTokenAsync(accessToken);
            if (string.IsNullOrEmpty(newAccessToken))
            {
                await ((UserJwtAuthenticationStateProvider)_authenticationStateProvider).NotifyUserLogout();
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            request.Headers.Authorization = new AuthenticationHeaderValue(ApplicationClientHelper.ApplicationBearerHeader, newAccessToken);
            try
            {
                result = await base.SendAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {

                return result;
            }

        }

        return result;
    }
}
