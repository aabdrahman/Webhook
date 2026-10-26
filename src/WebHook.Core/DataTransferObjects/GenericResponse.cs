using System.Net;
using System.Text.Json.Serialization;

namespace WebHook.Core.DataTransferObjects;

/// <summary>
/// Standard envelope returned by every service method and API endpoint.
/// </summary>
/// <remarks>
/// Services never throw for expected failures; they return a failed
/// <see cref="GenericResponse{T}"/> carrying the HTTP status code the
/// controller should respond with. Use <see cref="Success"/> and
/// <see cref="Failure"/> rather than the constructor.
/// </remarks>
/// <typeparam name="T">The type of the payload carried in <see cref="ResponseData"/>.</typeparam>
public class GenericResponse<T> where T : class
{
    /// <summary>The payload of the response, or <see langword="null"/> when there is none.</summary>
    [JsonPropertyName("responseData")]
    public T? ResponseData { get; private set; }
    /// <summary>A human-readable message describing the outcome.</summary>
    [JsonPropertyName("responseMessage")]
    public string ResponseMessage { get; private set; }
    /// <summary>Whether the operation succeeded.</summary>
    [JsonPropertyName("isSuccessful")]
    public bool IsSuccessful { get; private set; }
    /// <summary>Technical details of the error when the operation failed unexpectedly; otherwise <see langword="null"/>.</summary>
    [JsonPropertyName("errorDetail")]
    public ErrorDetail? ErrorDetail { get; private set; } = null;
    /// <summary>The HTTP status code that represents the outcome.</summary>
    [JsonPropertyName("httpStatusCode")]
    public HttpStatusCode HttpStatusCode { get; private set; }

    /// <summary>
    /// Initialises a new response. Prefer <see cref="Success"/> or <see cref="Failure"/>.
    /// </summary>
    /// <param name="responseData">The payload of the response.</param>
    /// <param name="responseMessage">A human-readable message describing the outcome.</param>
    /// <param name="httpStatusCode">The HTTP status code that represents the outcome.</param>
    /// <param name="isSuccessful">Whether the operation succeeded.</param>
    /// <param name="errorDetail">Optional technical details of the error.</param>
    public GenericResponse(T? responseData, string responseMessage, HttpStatusCode httpStatusCode, bool isSuccessful, ErrorDetail? errorDetail = null)
    {
        ResponseData = responseData;
        ResponseMessage = responseMessage;
        IsSuccessful = isSuccessful;
        ErrorDetail = errorDetail;
        HttpStatusCode = httpStatusCode;
    }

    /// <summary>
    /// Creates a successful response.
    /// </summary>
    /// <param name="responseData">The payload of the response.</param>
    /// <param name="responseMessage">A human-readable message describing the outcome.</param>
    /// <param name="httpStatusCode">The HTTP status code to respond with, such as <see cref="HttpStatusCode.OK"/> or <see cref="HttpStatusCode.Created"/>.</param>
    /// <returns>A response with <see cref="IsSuccessful"/> set to <see langword="true"/>.</returns>
    public static GenericResponse<T> Success(T? responseData, string responseMessage, HttpStatusCode httpStatusCode) => new GenericResponse<T>(responseData, responseMessage, httpStatusCode, true);

    /// <summary>
    /// Creates a failed response.
    /// </summary>
    /// <param name="responseData">An optional payload, typically <see langword="null"/> or a short failure label.</param>
    /// <param name="responseMessage">A human-readable message describing why the operation failed.</param>
    /// <param name="httpStatusCode">The HTTP status code to respond with, such as <see cref="HttpStatusCode.BadRequest"/>.</param>
    /// <param name="errorDetail">Technical details of the error, populated for unexpected exceptions.</param>
    /// <returns>A response with <see cref="IsSuccessful"/> set to <see langword="false"/>.</returns>
    public static GenericResponse<T> Failure(T? responseData, string responseMessage, HttpStatusCode httpStatusCode, ErrorDetail? errorDetail = null) => new GenericResponse<T>(responseData, responseMessage, httpStatusCode, false, errorDetail);
}


/// <summary>
/// Technical details of an unexpected error, attached to a failed <see cref="GenericResponse{T}"/>.
/// </summary>
public class ErrorDetail
{
    /// <summary>Additional detail, typically the inner exception message.</summary>
    public string ErrorDescription { get; set; }
    /// <summary>A short title for the error, typically the exception type name.</summary>
    public string ErrorTitle { get; set; }
    /// <summary>The main error message, typically the exception message.</summary>
    public string ErrorMessage { get; set; }
}