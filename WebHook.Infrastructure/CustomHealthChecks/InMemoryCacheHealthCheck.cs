using Microsoft.Extensions.Diagnostics.HealthChecks;
using WebHook.Core.Interfaces.Helpers;

namespace WebHook.Infrastructure.CustomHealthChecks;

public sealed class InMemoryCacheHealthCheck : IHealthCheck
{
    private readonly ICacheService _cacheService;

    public InMemoryCacheHealthCheck(ICacheService cacheService)
    {
        _cacheService = cacheService;
    }

    private string test_cache_key = "test=key";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var testValue = Random.Shared.GetHexString(6);

            bool setResult = await _cacheService.SetCacheItemAsync<string>(test_cache_key, testValue);

            if (!setResult)
            {
                return new HealthCheckResult(status: HealthStatus.Degraded, description: "Test item could not be cached successfully.");
            }

            string? itemFromCache = await _cacheService.GetItemsFromCacheAsync<string>(test_cache_key);
            if (string.IsNullOrWhiteSpace(itemFromCache))
            {
                return new HealthCheckResult(status: HealthStatus.Unhealthy, description: "Test item cached scuucessfulluy but could not be retieved.");
            }

            await _cacheService.RemoveItemsFromCacheAsync(test_cache_key);
            string? validateRemoval = await _cacheService.GetItemsFromCacheAsync<string>(test_cache_key);
            if (!string.IsNullOrWhiteSpace(validateRemoval))
            {
                return new HealthCheckResult(status: HealthStatus.Unhealthy, description: "Test item cached successfully but could not be removed.");
            }

            return new HealthCheckResult(status: HealthStatus.Healthy, description: "Cache service is working optimally.");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(status: HealthStatus.Degraded, exception: ex, description: $"An error occurred while checking caching health status: {ex.Message}");
        }
    }
}
