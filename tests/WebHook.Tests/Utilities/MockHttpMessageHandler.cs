using System.Net;

namespace WebHook.Tests.Utilities;

// =============================================================================
// Test doubles
// =============================================================================

/// <summary>
/// A controllable <see cref="HttpMessageHandler"/> for use in tests.
/// Supports a fixed status code for all requests, or per-URL status codes
/// via the <paramref name="responses"/> dictionary constructor.
/// Tracks how many times it was called via <see cref="CallCount"/>.
/// </summary>
public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode? _fixedStatusCode;
    private readonly string _responseBody;
    private readonly Dictionary<string, HttpStatusCode>? _urlResponses;
    private readonly Exception? _exceptionToThrow; // ← add this
    private readonly int _timespanDelay;
    private readonly Func<CapturedRequest, HttpResponseMessage>? _responder;

    public int CallCount { get; private set; }

    /// <summary>The most recent request, captured before it can be disposed. Null until a request is made.</summary>
    public CapturedRequest? LastRequest { get; private set; }

    // Fixed status code for all requests
    public MockHttpMessageHandler(
        HttpStatusCode statusCode,
        string responseBody = "", int timespanDelay = 0)
    {
        _fixedStatusCode = statusCode;
        _responseBody = responseBody;
        _timespanDelay = timespanDelay;
    }

    // Per-URL status codes
    public MockHttpMessageHandler(Dictionary<string, HttpStatusCode> responses)
    {
        _urlResponses = responses;
        _responseBody = "";
    }

    // Builds each response from the captured request, so a test can act as a real receiver
    public MockHttpMessageHandler(Func<CapturedRequest, HttpResponseMessage> responder)
    {
        _responder = responder;
        _responseBody = "";
    }

    // Throws exception for all requests ← new constructor
    public MockHttpMessageHandler(Exception exceptionToThrow)
    {
        _exceptionToThrow = exceptionToThrow;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_timespanDelay > 0)
            await Task.Delay(_timespanDelay);
        CallCount++;

        LastRequest = await CapturedRequest.FromAsync(request);

        if (_responder is not null)
            return _responder(LastRequest);

        // Throw if configured to do so
        if (_exceptionToThrow is not null)
            throw _exceptionToThrow;

        HttpStatusCode statusCode;

        if (_urlResponses is not null)
        {
            var baseUrl = $"{request.RequestUri!.Scheme}://{request.RequestUri.Host}/webhook";
            statusCode = _urlResponses.TryGetValue(baseUrl, out var code)
                ? code
                : HttpStatusCode.OK;
        }
        else
        {
            statusCode = _fixedStatusCode!.Value;
        }

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(_responseBody)
        };
    }
}

/// <summary>
/// A snapshot of an <see cref="HttpRequestMessage"/> taken while it is still alive.
/// </summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Uri">The request URI.</param>
/// <param name="Body">The request body, or an empty string if there was none.</param>
/// <param name="Headers">Request and content headers, keyed case-insensitively.</param>
public sealed record CapturedRequest(HttpMethod Method, Uri Uri, string Body, IReadOnlyDictionary<string, string> Headers)
{
    public static async Task<CapturedRequest> FromAsync(HttpRequestMessage request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in request.Headers)
            headers[header.Key] = string.Join(",", header.Value);

        string body = string.Empty;
        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
                headers[header.Key] = string.Join(",", header.Value);

            body = await request.Content.ReadAsStringAsync();
        }

        return new CapturedRequest(request.Method, request.RequestUri!, body, headers);
    }
}
