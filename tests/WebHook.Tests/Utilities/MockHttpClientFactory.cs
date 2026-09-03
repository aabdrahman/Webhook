namespace WebHook.Tests.Utilities;

/// <summary>
/// A minimal <see cref="IHttpClientFactory"/> that always returns an
/// <see cref="HttpClient"/> backed by the provided <see cref="MockHttpMessageHandler"/>.
/// </summary>
public sealed class MockHttpClientFactory : IHttpClientFactory
{
    private readonly MockHttpMessageHandler _handler;

    public MockHttpClientFactory(MockHttpMessageHandler handler) =>
        _handler = handler;

    public HttpClient CreateClient(string name = "") =>
        new HttpClient(_handler);
}
