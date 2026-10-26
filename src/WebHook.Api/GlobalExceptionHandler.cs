using Microsoft.AspNetCore.Diagnostics;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;

namespace WebHook.Api;
/// <summary>
/// This is the gloabl exception handler for the project. This ensures that any uncatched error that propagates to the application is handled globally and the generic response is returned and error details logged accordingly.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    /// <summary>
    /// The constructor for the global exception handler. This just initializes the logger by default with the context to enrich for the classname logging the details.
    /// </summary>
    public GlobalExceptionHandler()
    {
        _logger = Log.ForContext("ClassName", nameof(GlobalExceptionHandler));
    }
    private Serilog.ILogger _logger;
    /// <summary>
    /// This is the main method that handles the global exception propagated through.
    /// </summary>
    /// <param name="httpContext"></param>
    /// <param name="exception"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        _logger = _logger.ForContext("MethodName", nameof(TryHandleAsync));

        _logger.Warning("An unhandled exception is propagated to the global exception handler...");

        httpContext.Response.ContentType = "application/json";

        var errorDetail = new ErrorDetail();
        errorDetail.ErrorTitle = exception.GetType().Name;
        errorDetail.ErrorMessage = exception.Message;
        errorDetail.ErrorDescription = exception.InnerException?.Message ?? "";

        var responseBody = GenericResponse<string>.Failure("Inernal Server Error.", "An error occurred performing operation.", HttpStatusCode.InternalServerError, errorDetail);

        _logger.Error(exception, "An uncatched exception is propagated through the application. This is catastrophic");

        await httpContext.Response.WriteAsJsonAsync(responseBody, cancellationToken);

        return true;
    }
}
