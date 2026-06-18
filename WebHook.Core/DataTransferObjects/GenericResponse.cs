using System.Net;
using System.Text.Json.Serialization;

namespace WebHook.Core.DataTransferObjects;

public class GenericResponse<T> where T : class
{
    [JsonPropertyName("responseData")]
    private T? ResponseData { get; set; }
    [JsonPropertyName("responseMessage")]
    private string ResponseMessage { get; set; }
    [JsonPropertyName("isSuccessful")]
    private bool IsSuccessful { get; set; }
    [JsonPropertyName("errorDetail")]
    private ErrorDetail? ErrorDetail { get; set; } = null;
    [JsonPropertyName("httpStatusCode")]
    private HttpStatusCode HttpStatusCode { get; set; }

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