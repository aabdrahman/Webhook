using System.Text.Json;
using WebHook.Core.DataTransferObjects.OtpOperation;
/// <summary>
/// Buffers and inspects the request body on <c>POST /api/OtpOperation/validate-otp</c>
/// to extract the caller's email address, storing it in <see cref="HttpContext.Items"/>
/// under the key <c>otp-validation-email</c> for use by the rate limiter partition factory.
/// Falls back to <c>null</c> if the body cannot be read or parsed — the rate limiter
/// then falls back to IP-based partitioning.
/// </summary>
public class ValidateOtpEmailExtractionMiddleware : IMiddleware
{
    /// <summary>
    /// Extracts the email address from the request body when the request targets
    /// the OTP validation endpoint, then forwards the request to the next middleware.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="next">The next middleware delegate in the pipeline.</param>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Path.StartsWithSegments("/api/OtpOperation/validate-otp", StringComparison.OrdinalIgnoreCase))
        {
            context.Request.EnableBuffering();

            try
            {
                var body = await JsonSerializer.DeserializeAsync<OtpVerificationRequestDto>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var email = body?.EmailAddress?.Trim().ToLowerInvariant();

                context.Items["otp-validation-email"] = string.IsNullOrWhiteSpace(email) ? null : email;
            }
            catch
            {
                context.Items["otp-validation-email"] = null;
            }
            finally
            {
                context.Request.Body.Position = 0;
            }
        }

        await next(context);
    }
}