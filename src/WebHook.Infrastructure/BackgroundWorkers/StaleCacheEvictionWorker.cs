using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.Collections.Generic;
using System.Text;
using WebHook.Infrastructure.Persistence.ApplicationCache;

namespace WebHook.Infrastructure.BackgroundWorkers;

public class StaleCacheEvictionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public StaleCacheEvictionWorker(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    private ILogger _logger = Log.ForContext("ClassName", nameof(StaleCacheEvictionWorker));
    private const string _methodName = "MethodName";

    /// <summary>How often stale rows are deleted. Overridable so tests need not wait.</summary>
    protected virtual TimeSpan PollInterval => TimeSpan.FromSeconds(20);

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
