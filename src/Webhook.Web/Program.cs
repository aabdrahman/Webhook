using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System.Net.Http.Json;
using System.Text.Json;
using Webhook.Web;
using Webhook.Web.Helper;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

//This part is done in the Program.cs file to fetch the environment variables from the config.json file
var httpClient = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };

var environmentVariables = await httpClient.GetFromJsonAsync<EnvironmentVariables>("environment/config.json");

//Console.WriteLine($"AppBaseUrl: {environmentVariables?.AppBaseUrl}");
//Console.WriteLine($"ApiTimeOutInSeconds: {environmentVariables?.ApiTimeOutInSeconds}");

// Register the environment variables as a singleton service
if (environmentVariables != null)
{
    builder.Services.AddSingleton(environmentVariables);
}
else
{
    throw new Exception("Failed to load environment variables from config.json");
}

builder.Services.AddApplicationAuthenticationStateProvider();
builder.Services.ConfigureApplicationHttpClient(environmentVariables);
builder.Services.ConfigureApplicationLocalStorage();
builder.Services.AddApplicationHandlers();
builder.Services.AddApplicationCharts();

await builder.Build().RunAsync();
