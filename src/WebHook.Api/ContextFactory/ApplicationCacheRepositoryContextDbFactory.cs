using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WebHook.Infrastructure.Persistence.ApplicationCache;

namespace WebHook.Api.ContextFactory;

public class ApplicationCacheRepositoryContextDbFactory : IDesignTimeDbContextFactory<ApplicationCacheRepositoryContext>
{
    public ApplicationCacheRepositoryContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
                                    .SetBasePath(Directory.GetCurrentDirectory())
                                    .AddJsonFile("appsettings.json")
                                    .Build();

        var contextOptions = new DbContextOptionsBuilder<ApplicationCacheRepositoryContext>()
                                    .UseSqlite(configuration.GetConnectionString("ApplicationCacheDbConnection") ?? throw new ArgumentNullException("Application cache database configuration string not provided"))
                                    .EnableSensitiveDataLogging()
                                    .LogTo(Serilog.Log.Information, new[] { DbLoggerCategory.Database.Command.Name }, LogLevel.Information, DbContextLoggerOptions.SingleLine);

        return new ApplicationCacheRepositoryContext(contextOptions.Options);
    }
}
