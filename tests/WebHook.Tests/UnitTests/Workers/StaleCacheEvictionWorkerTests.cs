using Microsoft.Extensions.DependencyInjection;
using WebHook.Infrastructure.BackgroundWorkers;

namespace WebHook.Tests.UnitTests.Workers;

public sealed class StaleCacheEvictionWorkerTests : CacheWorkerTestBase
{
    private readonly FastStaleCacheEvictionWorker _worker;

    public StaleCacheEvictionWorkerTests()
    {
        _worker = new FastStaleCacheEvictionWorker(ScopeFactory);
    }

    public override void Dispose()
    {
        _worker.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        _worker.Dispose();
        base.Dispose();
    }

    [Fact]
    public async Task Tick_DeletesExpiredRowsAndKeepsValidOnes()
    {
        InsertRow("EXPIRED-1", "\"a\"", InPast());
        InsertRow("EXPIRED-2", "\"b\"", InPast(1));
        InsertRow("VALID", "\"c\"", InFuture());

        await _worker.StartAsync(CancellationToken.None);

        Assert.True(await WaitUntilAsync(() => Keys().SequenceEqual(["VALID"])));
    }

    [Fact]
    public async Task Tick_RowThatExpiresLater_IsDeletedOnALaterTick()
    {
        InsertRow("SOON", "\"a\"", DateTimeOffset.UtcNow.AddSeconds(1).ToUnixTimeSeconds());
        await _worker.StartAsync(CancellationToken.None);

        await Task.Delay(100);
        Assert.Contains("SOON", Keys());

        Assert.True(await WaitUntilAsync(() => Keys().Count == 0, timeoutMs: 5000));
    }

    [Fact]
    public async Task Tick_RowsInsertedWhileRunning_AreAlsoEvicted()
    {
        await _worker.StartAsync(CancellationToken.None);

        InsertRow("LATE-EXPIRED", "\"a\"", InPast());

        Assert.True(await WaitUntilAsync(() => Keys().Count == 0));
    }

    [Fact]
    public async Task Tick_EmptyTable_DoesNotThrowAndKeepsRunning()
    {
        await _worker.StartAsync(CancellationToken.None);
        await Task.Delay(150);

        Assert.False(_worker.ExecuteTask!.IsCompleted);
    }

    [Fact]
    public async Task Tick_DatabaseError_IsSwallowedAndWorkerRecoversOnNextTick()
    {
        DropTable();
        await _worker.StartAsync(CancellationToken.None);
        await Task.Delay(150);

        // The worker survived the failing ticks...
        Assert.False(_worker.ExecuteTask!.IsCompleted);

        // ...and resumes evicting once the table is back.
        CreateTable();
        InsertRow("EXPIRED", "\"a\"", InPast());
        Assert.True(await WaitUntilAsync(() => Keys().Count == 0));
    }

    [Fact]
    public async Task StopAsync_CompletesWithoutError()
    {
        await _worker.StartAsync(CancellationToken.None);

        await _worker.StopAsync(CancellationToken.None);

        // Cancelling the timer wait ends the loop with a cancelled (not faulted) task.
        Assert.True(_worker.ExecuteTask!.IsCompleted);
        Assert.False(_worker.ExecuteTask.IsFaulted);
    }

    private sealed class FastStaleCacheEvictionWorker(IServiceScopeFactory scopeFactory) : StaleCacheEvictionWorker(scopeFactory)
    {
        protected override TimeSpan PollInterval => TimeSpan.FromMilliseconds(20);
    }
}
