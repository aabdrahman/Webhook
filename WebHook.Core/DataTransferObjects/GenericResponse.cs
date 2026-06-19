using System.Net;
using System.Text.Json.Serialization;

namespace WebHook.Core.DataTransferObjects;

public class GenericResponse<T> where T : class
{
    [JsonPropertyName("responseData")]
    public T? ResponseData { get; private set; }
    [JsonPropertyName("responseMessage")]
    public string ResponseMessage { get; private set; }
    [JsonPropertyName("isSuccessful")]
    public bool IsSuccessful { get; private set; }
    [JsonPropertyName("errorDetail")]
    public ErrorDetail? ErrorDetail { get; private set; } = null;
    [JsonPropertyName("httpStatusCode")]
    public HttpStatusCode HttpStatusCode { get; private set; }

    public GenericResponse(T? responseData, string responseMessage, HttpStatusCode httpStatusCode, bool isSuccessful, ErrorDetail? errorDetail = null)
    {
        ResponseData = responseData;
        ResponseMessage = responseMessage;
        IsSuccessful = isSuccessful;
        ErrorDetail = errorDetail;
        HttpStatusCode = httpStatusCode;
    }

    public static GenericResponse<T> Success(T? responseData, string responseMessage, HttpStatusCode httpStatusCode) => new GenericResponse<T>(responseData, responseMessage, httpStatusCode, true);

    public static GenericResponse<T> Failure(T? responseData, string responseMessage, HttpStatusCode httpStatusCode, ErrorDetail? errorDetail = null) => new GenericResponse<T>(responseData, responseMessage, httpStatusCode, false, errorDetail);
}


public class ErrorDetail
{
    public string ErrorDescription { get; set; }
    public string ErrorTitle { get; set; }
    public string ErrorMessage { get; set; }
}