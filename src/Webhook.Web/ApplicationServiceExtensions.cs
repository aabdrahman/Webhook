using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using System.Text.Json;
using Webhook.Web.ApplicationAuthenticationHandler;
using Webhook.Web.Handlers.Authentication;
using Webhook.Web.Handlers.EventCatalog;
using Webhook.Web.Handlers.Subscription;
using Webhook.Web.Handlers.User;
using Webhook.Web.Helper;
using Webhook.Web.Services;

namespace Webhook.Web;

public static class ApplicationServiceExtensions
{
    internal static void AddApplicationHandlers(this IServiceCollection services)
    {
        services.AddScoped<RefreshTokenHandler>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<RegisterUserHandler>();
        services.AddScoped<RequestOtpHandler>();
        services.AddScoped<ValidateOtpHandler>();
        services.AddScoped<ResetPasswordHandler>();
        services.AddScoped<ChangePasswordHandler>();

        //Subscriptions
        services.AddScoped<GetSubscriptionsHandler>();
        services.AddScoped<GetSubscriptionDetailsHandler>();

        //Event Catalogs
        services.AddScoped<GetEventCatalogsHandler>();

        services.AddTransient<AuthenticationTokenHandler>();

        services.AddSingleton<ToastService>();
    }

    internal static void AddApplicationAuthenticationStateProvider(this IServiceCollection services)
    {
        services.AddAuthorizationCore();
        services.AddScoped<AuthenticationStateProvider, UserJwtAuthenticationStateProvider>();
    }

    internal static void ConfigureApplicationLocalStorage(this IServiceCollection services)
    {
        //Register blazor storage service
        services.AddBlazoredLocalStorage(opts =>
        {
            opts.JsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
        });
    }

    internal static void ConfigureApplicationHttpClient(this IServiceCollection services, EnvironmentVariables environmentVariables)
    {
        services.AddHttpClient(ApplicationClientHelper.ApplicationHttpClientNameSecure, client =>
        {
            client.BaseAddress = new Uri(environmentVariables.AppBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(environmentVariables.ApiTimeOutInSeconds);
        }).AddHttpMessageHandler<AuthenticationTokenHandler>();

        services.AddHttpClient(ApplicationClientHelper.ApplicationHttpClientNameOpen, client =>
        {
            client.BaseAddress = new Uri(environmentVariables.AppBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(environmentVariables.ApiTimeOutInSeconds);
        });
    }
}
