using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Threading.Channels;
using WebHook.Core.DataTransferObjects.ApplicationCache;
using WebHook.Infrastructure.BackgroundWorkers;

namespace WebHook.Tests.UnitTests.Workers;

public sealed class CachePersistenceWorkerTests : CacheWorkerTestBase
{
    private readonly Channel<ApplicationCacheEvent> _channel = Channel.CreateUnbounded<ApplicationCacheEvent>();
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
    private readonly FastCachePersistenceWorker _worker;

    public CachePersistenceWorkerTests()
    {
        _worker = new FastCachePersistenceWorker(_channel, ScopeFactory, _memoryCache);
    }

    public override void Dispose()
    {
        _worker.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        _worker.Dispose();
        _memoryCache.Dispose();
        base.Dispose();
    }

    // ------------------------------------------------------------ startup

    [Fact]
    public async Task StartAsync_DeletesExpiredRowsAndKeepsValidOnes()
    {
        InsertRow("EXPIRED", "\"a\"", InPast());
        InsertRow("VALID", "\"b\"", InFuture());

        await _worker.StartAsync(CancellationToken.None);

        Assert.Equal(["VALID"], Keys());
    }

    [Fact]
    public async Task StartAsync_EmptyTable_DoesNotThrow()
    {
        await _worker.StartAsync(CancellationToken.None);

        Assert.Empty(Keys());
    }

    // ------------------------------------------------------- persistence

    [Fact]
    public async Task QueuedEvent_IsPersistedWithUpperCaseKeyAndExpiry()
    {
        var value = JsonSerializer.Serialize(Guid.NewGuid());
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _worker.StartAsync(CancellationToken.None);

        await _channel.Writer.WriteAsync(new ApplicationCacheEvent("user@x.com", value, 120));

        Assert.True(await WaitUntilAsync(() => GetRow("USER@X.COM") is not null));
        var row = GetRow("USER@X.COM")!.Value;
        Assert.Equal(value, row.Value);
        Assert.InRange(row.CreatedAt, before, before + 5);
        Assert.InRange(row.ExpiresAt - row.CreatedAt, 119, 121);
    }

    [Fact]
    public async Task QueuedEvent_WithSameKey_ReplacesTheExistingRow()
    {
        await _worker.StartAsync(CancellationToken.None);

        await _channel.Writer.WriteAsync(new ApplicationCacheEvent("k", "\"first\"", 300));
        await _channel.Writer.WriteAsync(new ApplicationCacheEvent("k", "\"second\"", 300));

        Assert.True(await WaitUntilAsync(() => GetRow("K")?.Value == "\"second\""));
        Assert.Single(Keys());
    }

    [Fact]
    public async Task QueuedEvents_AreAllPersisted()
    {
        await _worker.StartAsync(CancellationToken.None);

        for (int i = 0; i < 20; i++)
            await _channel.Writer.WriteAsync(new ApplicationCacheEvent($"key-{i}", $"\"{i}\"", 300));

        Assert.True(await WaitUntilAsync(() => Keys().Count == 20));
    }

    [Fact]
    public async Task QueuedEvent_ValueIsPlacedInMemoryCache()
    {
        var value = JsonSerializer.Serialize(Guid.NewGuid());
        await _worker.StartAsync(CancellationToken.None);

        await _channel.Writer.WriteAsync(new ApplicationCacheEvent("user@x.com", value, 300));

        Assert.True(await WaitUntilAsync(() => _memoryCache.TryGetValue("user@x.com", out _)));
        Assert.Equal(value, _memoryCache.Get<string>("user@x.com"));
    }

    [Fact]
    public async Task QueuedEvent_MemoryEntryUsesTheExpiryInSeconds_NotMinutes()
    {
        await _worker.StartAsync(CancellationToken.None);

        await _channel.Writer.WriteAsync(new ApplicationCacheEvent("short", "\"v\"", 1));
        // Wait for the row (written just before the memory entry), then leave the entry untouched:
        // every read renews a sliding expiry, so polling it would keep it alive.
        Assert.True(await WaitUntilAsync(() => GetRow("SHORT") is not null));
        await Task.Delay(1800);

        // Sliding expiry of 1 second: gone by now (it would live for 1 minute if seconds were treated as minutes).
        Assert.False(_memoryCache.TryGetValue("short", out _));
    }

    [Fact]
    public async Task EventsQueuedBeforeStart_AreProcessedOnceTheWorkerStarts()
    {
        await _channel.Writer.WriteAsync(new ApplicationCacheEvent("early", "\"v\"", 300));

        await _worker.StartAsync(CancellationToken.None);

        Assert.True(await WaitUntilAsync(() => GetRow("EARLY") is not null));
    }

    // ---------------------------------------------------------- shutdown

    [Fact]
    public async Task StopAsync_WhileIdle_CompletesWithoutError()
    {
        await _worker.StartAsync(CancellationToken.None);

        await _worker.StopAsync(CancellationToken.None);

        Assert.True(_worker.ExecuteTask is null || _worker.ExecuteTask.IsCompleted);
    }

    private sealed class FastCachePersistenceWorker(Channel<ApplicationCacheEvent> channel, IServiceScopeFactory scopeFactory, IMemoryCache memoryCache)
        : CachePersistenceWorker(channel, scopeFactory, memoryCache)
    {
        protected override TimeSpan PollInterval => TimeSpan.FromMilliseconds(20);
    }
}
