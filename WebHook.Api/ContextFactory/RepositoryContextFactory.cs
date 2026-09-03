using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Api.ContextFactory;

public class RepositoryContextFactory : IDesignTimeDbContextFactory<RepositoryContext>
{
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
