using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.Collections.Generic;
using System.Text;
using WebHook.Infrastructure.Persistence.ApplicationCache;

namespace WebHook.Infrastructure.BackgroundWorkers;

/// <summary>
/// Periodically deletes expired rows from the SQLite cache table.
/// </summary>
/// <remarks>
/// Reads already ignore expired rows, so this only stops the table growing. Each tick runs
/// <c>DELETE ... WHERE ExpiresAt &lt; now</c> in a new scope. Errors are logged and the next tick tries again,
/// so a temporary database problem does not stop the worker.
/// </remarks>
public class StaleCacheEvictionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    /// <summary>
    /// Initialises a new instance of the <see cref="StaleCacheEvictionWorker"/> class.
    /// </summary>
    /// <param name="serviceScopeFactory">Creates a scope per tick to resolve the cache DbContext.</param>
    public StaleCacheEvictionWorker(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    private ILogger _logger = Log.ForContext("ClassName", nameof(StaleCacheEvictionWorker));
    private const string _methodName = "MethodName";

    /// <summary>How often stale rows are deleted. Defaults to 20 seconds; overridable so tests need not wait.</summary>
    protected virtual TimeSpan PollInterval => TimeSpan.FromSeconds(20);

    /// <summary>Deletes expired cache rows every <see cref="PollInterval"/> until the host stops.</summary>
    /// <param name="stoppingToken">Signalled when the host is shutting down.</param>
    /// <returns>A task that completes when the worker stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger = _logger.ForContext(_methodName, nameof(ExecuteAsync));

        using var timer = new PeriodicTimer(PollInterval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            _logger.Information("Beging processing stale cache eviction......");

            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<ApplicationCacheRepositoryContext>();

                string query = @"DELETE FROM ApplicationCacheLayerEntities WHERE ExpiresAt < {0}";

                var totalClearedCache = await ctx.Database.ExecuteSqlRawAsync(query, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                _logger.Information("Total stale cache cleared - {0}", totalClearedCache);

            }
            catch (Exception ex)
            {
                _logger.Error(ex, "An error occurred while clearing stale cache...");

            }
        }
    }
}
