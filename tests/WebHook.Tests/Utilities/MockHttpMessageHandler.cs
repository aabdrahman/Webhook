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

    public int CallCount { get; private set; }

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
