using Serilog;
using WebHook.Api.ServiceExtensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

//Serilog Logger implementation
string logFromConfig = builder.Configuration.GetValue<string>("");
string logFilePath = string.IsNullOrWhiteSpace(logFromConfig) ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "log-.txt") : Path.Combine(logFromConfig, "log-.txt");

Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()   
                    .Enrich.FromLogContext()
                    .WriteTo.File(path: logFilePath, rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true,
                                    fileSizeLimitBytes: 5_000_000, restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Verbose, 
                                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.ffff zzzz} || {Level:u3}] || [{ClassName}].[{MethodName}] - {Message:lj}{NewLine}{Exception}{NewLine}")
                    .CreateLogger();

builder.Services.ConfigureDatabaseConnection(builder.Configuration);

builder.Services.AddControllers();
builder.Services.ConfigureApplicationServices();
builder.Services.AddConfigurationModels(builder.Configuration);
//builder.Services.ConfigureMassTransit();
builder.Services.ConfigureApplicationChannels();
builder.Services.ConfigureApplicationWorkers();
builder.Services.ConfigureHttpClient();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.ConfigureSwagger();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseSwaggerUI(opts =>
{
    opts.RoutePrefix = "webhookapi";
    opts.SwaggerEndpoint("/openapi/v1.json", "Webhook API V1");
});

app.UseReDoc(opts =>
{
    opts.SpecUrl("/openapi/v1.json");
    opts.DocumentTitle = "Webhook API v1 Documentation";
    opts.RoutePrefix = "webhookapi/api-docs";
});

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
