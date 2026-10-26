using System.Text.Json;
using WebHook.Core.DataTransferObjects.Authentication;

/// <summary>
/// Buffers and inspects the request body on <c>POST /api/Authentication/request-otp</c>
/// to extract the caller's email address, storing it in <see cref="HttpContext.Items"/>
/// under the key <c>otp-request-email</c> for use by the rate limiter partition factory.
/// Falls back to <c>null</c> if the body cannot be read or parsed — the rate limiter
/// then falls back to IP-based partitioning.
/// </summary>
public class RequestOtpEmailExtractionMiddleware : IMiddleware
{
    /// <summary>
    /// Extracts the email address from the request body when the request targets
    /// the OTP request endpoint, then forwards the request to the next middleware.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="next">The next middleware delegate in the pipeline.</param>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Path.StartsWithSegments("/api/Authentication/request-otp", StringComparison.OrdinalIgnoreCase))
        {
            context.Request.EnableBuffering();

            try
            {
                var body = await JsonSerializer.DeserializeAsync<RequestOtpDto>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var email = body?.UserNameOrEmailAddress?.Trim().ToLowerInvariant();

                context.Items["otp-request-email"] = string.IsNullOrWhiteSpace(email) ? null : email;
            }
            catch
            {
                context.Items["otp-request-email"] = null;
            }
            finally
            {
                context.Request.Body.Position = 0;
            }
        }

        await next(context);
    }
}
