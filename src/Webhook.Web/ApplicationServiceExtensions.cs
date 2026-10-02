using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using System.Text.Json;
using Webhook.Web.ApplicationAuthenticationHandler;
using Webhook.Web.Handlers.Admin;
using Webhook.Web.Handlers.Authentication;
using Webhook.Web.Handlers.Delivery;
using Webhook.Web.Handlers.EventCatalog;
using Webhook.Web.Handlers.ReportingAnalytics;
using Webhook.Web.Handlers.ServiceClient;
using Webhook.Web.Handlers.ServiceClient.ServiceClientEventCatalog;
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
        services.AddScoped<DeleteSubscriptionHandler>();
        services.AddScoped<CreateSubscriptionHandler>();
        services.AddScoped<ReactivateSubscriptionHandler>();

        //Subscription Events
        services.AddScoped<AddEventToSubscriptionHandler>();
        services.AddScoped<UnsubscribeFromEventHandler>();

        //Event Catalogs
        services.AddScoped<GetEventCatalogsHandler>();
        services.AddScoped<CreateEventCatalogHandler>();
        services.AddScoped<ModifyEventCatalogStatusHandler>();

        //Service Clients
        services.AddScoped<GetOnboardedServiceClientsHandler>();
        services.AddScoped<OnboardNewClientHandler>();
        services.AddScoped<GetClientDetailsByClientIdHandler>();
        services.AddScoped<DeleteServiceClientHandler>();
        services.AddScoped<ReactivateServiceClientHandler>();
        services.AddScoped<RequestNewServiceClientKeyHandler>();

        //Service Client Event Catalogs
        services.AddScoped<GetServiceClientSubscribedCatalogsHandler>();
        services.AddScoped<SubscribeToEventCatalogHandler>();
        services.AddScoped<UnSubscribeFromEventCatalogHandler>();

        //Users
        services.AddScoped<GetAllUsersHandler>();
        services.AddScoped<RevokeUserTokensHandler>();
        services.AddScoped<DeactivateUserProfileHandler>();
        services.AddScoped<ReactivateUserProfileHandler>();
        services.AddScoped<AssignNewRoleHandler>();
        services.AddScoped<GetUserDetailsHandler>();

        //Delivery
        services.AddScoped<GetDeliveriesHandler>();
        services.AddScoped<RequestDeadLetterDeliveryRetryHandler>();
        services.AddScoped<GetDeliveryDeadLettersHandler>();
        services.AddScoped<GetDeliveryDetailsHandler>();
        services.AddScoped<GetDeliveryAttemptsHandler>();

        //Authentication
        services.AddTransient<AuthenticationTokenHandler>();

        //Admin Health Check
        services.AddScoped<HealthCheckStatusHandler>();

        //Utilities
        services.AddSingleton<ToastService>();

        //Reporting Analytics
        services.AddScoped<GetMostRecentEventsHandler>();
        services.AddScoped<GetEventCatalogAnalyticsHandler>();
        services.AddScoped<GetTopFailingSubscriptionsHandler>();
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
