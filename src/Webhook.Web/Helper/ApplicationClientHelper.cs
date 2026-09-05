namespace Webhook.Web.Helper;

public static class ApplicationClientHelper
{
    public static string StorageKey = "tokenDetails";
    public static string OperationTokenKey = "operationToken";
    public static string ApplicationHttpClientNameSecure = "Webhook.Web.ServerAPI.Secure";
    public static string ApplicationHttpClientNameOpen = "Webhook.Web.ServerAPI.Open";
    public static string ApplicationBearerHeader = "Bearer";
    public static string RegisteredUserEmailKey = "registeredUserEmail";
    public static string RememberMeKey = "BrowserLoggedInKey";
    public static string OtpRequestedUser = "OtpRequestedUserEmail";
}