using System.Text.Json;
using Webhook.Web.Helper;

namespace Webhook.Web.Handlers.Admin;

public class HealthCheckStatusHandler
{
    private readonly HttpClient _httpClient;
    public HealthCheckStatusHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<HealthCheckDetails?> HandleAsync()
    {
        try
        {
            var httpResponse = await _httpClient.GetAsync("Admin/_health");
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            //Console.WriteLine(responseContent);

            HealthCheckDetails response = JsonSerializer.Deserialize<HealthCheckDetails>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ??
                throw new InvalidOperationException("Http Response could not be deserialized.");
            Console.WriteLine(JsonSerializer.Serialize(response));
            return response;
        }
        catch (Exception ex)
        {
            return null;
        }
    }
}



public class HealthCheckDetails
{
    public string status { get; set; }
    public List<Check> checks { get; set; } = [];
    public float totalDurationMs { get; set; }
}

public class Check
{
    public string name { get; set; }
    public string status { get; set; }
    public float duration { get; set; }
    public object? exception { get; set; }
    public string description { get; set; }
    public string[] tags { get; set; }
}
