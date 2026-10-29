using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using System.Text.Json;
using WebHook.Core.DataTransferObjects.ApplicationCache;
using WebHook.Core.EventContracts.Publishers;
using WebHook.Infrastructure.Persistence.ApplicationCache;
using WebHook.Infrastructure.Utilities;

namespace WebHook.Tests.UnitTests.Helpers;

/// <summary>
/// Tests <see cref="CacheService"/> against a real in-memory SQLite database (the persistence layer uses raw
/// SQL, which cannot be mocked) and a real <see cref="MemoryCache"/>. Only the publisher is mocked.
/// </summary>
public sealed class CacheServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationCacheRepositoryContext _context;
    private readonly IMemoryCache _memoryCache;
    private readonly Mock<IApplicationPublisher> _publisherMock;
    private readonly CacheService _sut;

    public CacheServiceTests()
    {
        // A named in-memory database lives only as long as this connection stays open.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using (var createTable = _connection.CreateCommand())
        {
            createTable.CommandText = @"CREATE TABLE ApplicationCacheLayerEntities (
                                            CacheKey TEXT NOT NULL,
                                            CacheValue BLOB NOT NULL,
                                            CreatedAt INTEGER NOT NULL,
                                            ExpiresAt INTEGER NOT NULL);
                                        CREATE UNIQUE INDEX IX_ApplicationCacheLayerEntities_CacheKey ON ApplicationCacheLayerEntities (CacheKey);";
            createTable.ExecuteNonQuery();
        }

        _context = new ApplicationCacheRepositoryContext(new DbContextOptionsBuilder<ApplicationCacheRepositoryContext>().UseSqlite(_connection).Options);
        _memoryCache = new MemoryCache(new MemoryCacheOptions());

        _publisherMock = new Mock<IApplicationPublisher>();
        _publisherMock.Setup(x => x.QueueItemToCache(It.IsAny<ApplicationCacheEvent>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _sut = new CacheService(_memoryCache, _context, _publisherMock.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        _memoryCache.Dispose();
        _connection.Dispose();
    }

    private void InsertRow(string upperCaseKey, string jsonValue, long expiresAt)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "INSERT INTO ApplicationCacheLayerEntities (CacheKey, CacheValue, CreatedAt, ExpiresAt) VALUES ($key, $value, $createdAt, $expiresAt)";
        command.Parameters.AddWithValue("$key", upperCaseKey);
        command.Parameters.AddWithValue("$value", jsonValue);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$expiresAt", expiresAt);
        command.ExecuteNonQuery();
    }

    private long CountRows()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ApplicationCacheLayerEntities";
        return (long)command.ExecuteScalar()!;
    }

    private static long InFuture(int seconds = 300) => DateTimeOffset.UtcNow.AddSeconds(seconds).ToUnixTimeSeconds();
    private static long InPast(int seconds = 300) => DateTimeOffset.UtcNow.AddSeconds(-seconds).ToUnixTimeSeconds();

    // ---------------------------------------------------------------- Get

    [Fact]
    public async Task GetItemsFromCacheAsync_NonExistentKey_ReturnsNull()
    {
        var result = await _sut.GetItemsFromCacheAsync<string>("products");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetItemsFromCacheAsync_NonExistentKeyForValueType_ReturnsDefault()
    {
        var result = await _sut.GetItemsFromCacheAsync<Guid>("missing");

        Assert.Equal(default, result);
    }

    [Fact]
    public async Task GetItemsFromCacheAsync_ItemInMemory_ReturnsItWithoutQueryingDatabase()
    {
        var value = Guid.NewGuid();
        _memoryCache.Set("user@x.com", value);

        var result = await _sut.GetItemsFromCacheAsync<Guid>("user@x.com");

        Assert.Equal(value, result);
    }

    [Fact]
    public async Task GetItemsFromCacheAsync_NotInMemoryButInDatabase_ReturnsDeserializedValue()
    {
        var value = Guid.NewGuid();
        InsertRow("USER@X.COM", JsonSerializer.Serialize(value), InFuture());

        var result = await _sut.GetItemsFromCacheAsync<Guid>("user@x.com");

        Assert.Equal(value, result);
    }

    [Fact]
    public async Task GetItemsFromCacheAsync_DatabaseLookupIsCaseInsensitiveOnKey()
    {
        var value = Guid.NewGuid();
        InsertRow("USER@X.COM", JsonSerializer.Serialize(value), InFuture());

        var result = await _sut.GetItemsFromCacheAsync<Guid>("User@X.com");

        Assert.Equal(value, result);
    }

    [Fact]
    public async Task GetItemsFromCacheAsync_ExpiredDatabaseRow_ReturnsDefault()
    {
        InsertRow("USER@X.COM", JsonSerializer.Serialize(Guid.NewGuid()), InPast());

        var result = await _sut.GetItemsFromCacheAsync<Guid>("user@x.com");

        Assert.Equal(default, result);
    }

    [Fact]
    public async Task GetItemsFromCacheAsync_ComplexTypeInDatabase_IsDeserialized()
    {
        InsertRow("PROFILE", JsonSerializer.Serialize(new Sample("abc", 7)), InFuture());

        var result = await _sut.GetItemsFromCacheAsync<Sample>("profile");

        Assert.Equal(new Sample("abc", 7), result);
    }

    [Fact]
    public async Task GetItemsFromCacheAsync_CorruptJsonInDatabase_ReturnsDefaultInsteadOfThrowing()
    {
        InsertRow("BAD", "not-json{", InFuture());

        var result = await _sut.GetItemsFromCacheAsync<Guid>("bad");

        Assert.Equal(default, result);
    }

    [Fact]
    public async Task GetItemsFromCacheAsync_DatabaseUnavailable_ReturnsDefaultInsteadOfThrowing()
    {
        await _context.Database.CloseConnectionAsync();
        _connection.Dispose();

        var result = await _sut.GetItemsFromCacheAsync<Guid>("anything");

        Assert.Equal(default, result);
    }

    // ---------------------------------------------------------------- Set

    [Fact]
    public async Task SetCacheItemAsync_Queued_ReturnsTrueAndPublishesSerializedEvent()
    {
        var value = Guid.NewGuid();
        ApplicationCacheEvent? published = null;
        _publisherMock.Setup(x => x.QueueItemToCache(It.IsAny<ApplicationCacheEvent>(), It.IsAny<CancellationToken>()))
                      .Callback<ApplicationCacheEvent, CancellationToken>((e, _) => published = e)
                      .ReturnsAsync(true);

        var result = await _sut.SetCacheItemAsync("user@x.com", value, expiresAfter: 120);

        Assert.True(result);
        Assert.NotNull(published);
        Assert.Equal("user@x.com", published!.cacheKey);
        Assert.Equal(JsonSerializer.Serialize(value), published.cacheValue);
        Assert.Equal(120, published.cacheExpiration);
    }

    [Fact]
    public async Task SetCacheItemAsync_DefaultExpiry_Is300Seconds()
    {
        ApplicationCacheEvent? published = null;
        _publisherMock.Setup(x => x.QueueItemToCache(It.IsAny<ApplicationCacheEvent>(), It.IsAny<CancellationToken>()))
                      .Callback<ApplicationCacheEvent, CancellationToken>((e, _) => published = e)
                      .ReturnsAsync(true);

        await _sut.SetCacheItemAsync("k", "v");

        Assert.Equal(300, published!.cacheExpiration);
    }

    [Fact]
    public async Task SetCacheItemAsync_Queued_ValueIsImmediatelyReadableFromMemory()
    {
        var value = Guid.NewGuid();

        await _sut.SetCacheItemAsync("user@x.com", value);
        var result = await _sut.GetItemsFromCacheAsync<Guid>("user@x.com");

        Assert.Equal(value, result);
    }

    [Fact]
    public async Task SetCacheItemAsync_Queued_MemoryEntryExpiresAfterRequestedSeconds()
    {
        await _sut.SetCacheItemAsync("short-lived", Guid.NewGuid(), expiresAfter: 1);

        await Task.Delay(TimeSpan.FromMilliseconds(1500));
        var result = await _sut.GetItemsFromCacheAsync<Guid>("short-lived");

        // Memory has expired and nothing was persisted (the worker is not running in this test).
        Assert.Equal(default, result);
    }

    [Fact]
    public async Task SetCacheItemAsync_NotQueued_ReturnsFalseAndDoesNotCache()
    {
        _publisherMock.Setup(x => x.QueueItemToCache(It.IsAny<ApplicationCacheEvent>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _sut.SetCacheItemAsync("user@x.com", Guid.NewGuid());

        Assert.False(result);
        Assert.False(_memoryCache.TryGetValue("user@x.com", out _));
    }

    [Fact]
    public async Task SetCacheItemAsync_PublisherThrows_ReturnsFalse()
    {
        _publisherMock.Setup(x => x.QueueItemToCache(It.IsAny<ApplicationCacheEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("channel closed"));

        var result = await _sut.SetCacheItemAsync("user@x.com", Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public async Task SetCacheItemAsync_ComplexType_RoundTripsThroughMemory()
    {
        await _sut.SetCacheItemAsync("profile", new Sample("abc", 7));

        var result = await _sut.GetItemsFromCacheAsync<Sample>("profile");

        Assert.Equal(new Sample("abc", 7), result);
    }

    // ------------------------------------------------------------- Remove

    [Fact]
    public async Task RemoveItemsFromCacheAsync_KeyNotCached_DoesNotThrow()
    {
        await _sut.RemoveItemsFromCacheAsync("never-set");

        Assert.Equal(default, await _sut.GetItemsFromCacheAsync<Guid>("never-set"));
    }

    [Fact]
    public async Task RemoveItemsFromCacheAsync_ItemInMemoryOnly_IsRemoved()
    {
        await _sut.SetCacheItemAsync("k", Guid.NewGuid());

        await _sut.RemoveItemsFromCacheAsync("k");

        Assert.Equal(default, await _sut.GetItemsFromCacheAsync<Guid>("k"));
    }

    [Fact]
    public async Task RemoveItemsFromCacheAsync_ItemInDatabaseOnly_IsRemoved()
    {
        InsertRow("K", JsonSerializer.Serialize(Guid.NewGuid()), InFuture());

        await _sut.RemoveItemsFromCacheAsync("k");

        Assert.Equal(0, CountRows());
        Assert.Equal(default, await _sut.GetItemsFromCacheAsync<Guid>("k"));
    }

    [Fact]
    public async Task RemoveItemsFromCacheAsync_ItemInMemoryAndDatabase_IsRemovedFromBoth()
    {
        var value = Guid.NewGuid();
        _memoryCache.Set("user@x.com", value);
        InsertRow("USER@X.COM", JsonSerializer.Serialize(value), InFuture());

        await _sut.RemoveItemsFromCacheAsync("user@x.com");

        Assert.False(_memoryCache.TryGetValue("user@x.com", out _));
        Assert.Equal(0, CountRows());
    }

    [Fact]
    public async Task RemoveItemsFromCacheAsync_MultipleKeys_RemovesOnlyTheRequestedOnes()
    {
        string[] keys = ["a", "b", "c"];
        foreach (var key in keys)
        {
            _memoryCache.Set(key, Guid.NewGuid());
            InsertRow(key.ToUpperInvariant(), JsonSerializer.Serialize(Guid.NewGuid()), InFuture());
        }
        _memoryCache.Set("keep", Guid.NewGuid());
        InsertRow("KEEP", JsonSerializer.Serialize(Guid.NewGuid()), InFuture());

        await _sut.RemoveItemsFromCacheAsync(keys);

        foreach (var key in keys)
            Assert.Equal(default, await _sut.GetItemsFromCacheAsync<Guid>(key));

        Assert.True(_memoryCache.TryGetValue("keep", out _));
        Assert.Equal(1, CountRows());
    }

    [Fact]
    public async Task RemoveItemsFromCacheAsync_KeyContainingQuotesAndSqlCharacters_IsHandledSafely()
    {
        const string hostile = "x'); DROP TABLE ApplicationCacheLayerEntities;--";
        InsertRow("KEEP", JsonSerializer.Serialize(Guid.NewGuid()), InFuture());

        await _sut.RemoveItemsFromCacheAsync(hostile);

        Assert.Equal(1, CountRows());
    }

    private sealed record Sample(string Name, int Count);
}
