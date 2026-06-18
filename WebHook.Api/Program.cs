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

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseReDoc(opts =>
{
    opts.SpecUrl("/openapi/v1.json");
});

app.UseSwaggerUI(opts =>
{
    opts.SwaggerEndpoint("/openapi/v1.json", "Webhook API V1");
});

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
