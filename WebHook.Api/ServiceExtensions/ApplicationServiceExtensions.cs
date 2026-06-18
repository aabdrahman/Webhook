using Microsoft.EntityFrameworkCore;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Api.ServiceExtensions;

public static class ApplicationServiceExtensions
{
    internal static void ConfigureSwagger(this IServiceCollection services)
    {
        //services.AddSwaggerGen(opts =>
        //{
        //    opts.SwaggerDoc("v1", new Microsoft.OpenApi()
        //    {
        //        Contact = new Microsoft.OpenApi.OpenApiContact() { Email = "akandeabdrahman@gmail.com", Name = "Akande Abdrahman", Url = new Uri("https://github.com/aabdrahman") },
        //        Description = "API for webhook implementation",
        //        Title = "Webhook API",
        //        Version = "v1",
        //        Summary = "Swager documentation for the webhook api"
        //    });
        //});

    }

    internal static void ConfigureDatabaseConnection(this IServiceCollection services, IConfiguration configuration) 
    {
        services.AddDbContext<RepositoryContext>(opts =>
        {
            opts.UseNpgsql(configuration.GetConnectionString("DbConnection") ?? throw new ArgumentNullException("Database configuration string not provided"), v => v.SetPostgresVersion(18, 0))
                .EnableSensitiveDataLogging()
                .LogTo(Serilog.Log.Information, 
                        new[] { DbLoggerCategory.Database.Command.Name }, 
                        minimumLevel: LogLevel.Information, 
                        options: Microsoft.EntityFrameworkCore.Diagnostics.DbContextLoggerOptions.SingleLine
                 );
        });
    }
}
