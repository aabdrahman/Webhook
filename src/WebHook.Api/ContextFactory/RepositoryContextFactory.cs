using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Api.ContextFactory;

/// <summary>
/// Creates a <see cref="RepositoryContext"/> for EF Core design-time tools such as
/// <c>dotnet ef migrations</c> and <c>dotnet ef database update</c>.
/// </summary>
public class RepositoryContextFactory : IDesignTimeDbContextFactory<RepositoryContext>
{
    /// <summary>
    /// Creates a context using the <c>DbConnection</c> connection string from <c>appsettings.json</c>
    /// in the current directory.
    /// </summary>
    /// <param name="args">Arguments passed by the design-time tools. Not used.</param>
    /// <returns>A new <see cref="RepositoryContext"/>.</returns>
    /// <exception cref="ArgumentNullException">The connection string is missing.</exception>
    public RepositoryContext CreateDbContext(string[] args)
    {

        var configuration = new ConfigurationBuilder()
                                    .SetBasePath(Directory.GetCurrentDirectory())
                                    .AddJsonFile("appsettings.json")
                                    .Build();

        var contextOptions = new DbContextOptionsBuilder<RepositoryContext>()
                                    .UseNpgsql(configuration.GetConnectionString("DbConnection") ?? throw new ArgumentNullException("Connection string is not specified."))
                                    .EnableSensitiveDataLogging()
                                    .LogTo(Serilog.Log.Information, new[] { DbLoggerCategory.Database.Command.Name }, LogLevel.Information, DbContextLoggerOptions.SingleLine);

        return new RepositoryContext(contextOptions.Options);
    }
}
