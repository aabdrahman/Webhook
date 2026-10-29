using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WebHook.Infrastructure.Persistence.ApplicationCache;

namespace WebHook.Api.ContextFactory;

/// <summary>
/// Creates an <see cref="ApplicationCacheRepositoryContext"/> for EF Core design-time tools such as
/// <c>dotnet ef migrations</c> and <c>dotnet ef database update --context ApplicationCacheRepositoryContext</c>.
/// </summary>
public class ApplicationCacheRepositoryContextDbFactory : IDesignTimeDbContextFactory<ApplicationCacheRepositoryContext>
{
    /// <summary>
    /// Creates a context using the <c>ApplicationCacheDbConnection</c> SQLite connection string from
    /// <c>appsettings.json</c> in the current directory.
    /// </summary>
    /// <param name="args">Arguments passed by the design-time tools. Not used.</param>
    /// <returns>A new <see cref="ApplicationCacheRepositoryContext"/>.</returns>
    /// <exception cref="ArgumentNullException">The connection string is missing.</exception>
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
