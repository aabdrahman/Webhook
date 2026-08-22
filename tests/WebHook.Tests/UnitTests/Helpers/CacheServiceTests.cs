using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Text;
using WebHook.Infrastructure.Utilities;

namespace WebHook.Tests.UnitTests.Helpers;

public class CacheServiceTests
{
    private readonly IMemoryCache _cache;
    private readonly CacheService _cacheService;

    public CacheServiceTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _cacheService = new CacheService(_cache);
    }

    private CacheService CreateSut() => new CacheService(_cache);

    [Fact]
    public async Task GetItemsFromCacheAsync_NonExistentKey_ShouldReturnNull()
    {
        //Arrange
        string cacaheKey = "products";
        var sut = CreateSut();

        //Act
        var result = await sut.GetItemsFromCacheAsync<string>(cacaheKey);

        //Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetItemsFromCacheAsync_CacheExists_SHouldReturnValidType()
    {
        //Arrange
        var key = "random-key";
        var cacheValue = Guid.NewGuid();
        var sut = CreateSut();

        //Act
        var setResult = await sut.SetCacheItemAsync(key, cacheValue);
        var result = await sut.GetItemsFromCacheAsync<Guid>(key);

        //Assert
        Assert.True(setResult);
        Assert.NotEqual(default(Guid), result);
        Assert.IsType<Guid>(result);
    }

    [Fact]
    public async Task RemoveItemsFromCacheAsync_CacheNotExist_ShouldBeSuccessful()
    {
        //Arrange
        var key = "random-key";
        var cacheValue = Guid.NewGuid();
        var sut = CreateSut();

        //Act
        await sut.RemoveItemsFromCacheAsync(key);
        var getResult = await sut.GetItemsFromCacheAsync<Guid>(key);

        //Assert
        Assert.Equal(default(Guid), getResult);
    }

    [Fact]
    public async Task RemoveItemsFromCacheAsync_CacheExists_ShouldBeSuccessful()
    {
        //Arrange
        var key = "random-key";
        var cacheValue = Guid.NewGuid();
        var sut = CreateSut();

        var setResult = await sut.SetCacheItemAsync<Guid>(key, cacheValue);
        Assert.True(setResult);

        //Act
        await sut.RemoveItemsFromCacheAsync(key);
        var getResult = await sut.GetItemsFromCacheAsync<Guid>(key);

        //Assert
        Assert.Equal(default(Guid), getResult);
    }

    [Fact]
    public async Task RemoveItemsFromCacheAsync_MultipleKeys_RemovedSuccessfully()
    {
        //Arrange
        string[] keys = { "random-key-1", "random-key-2", "random-key-3" };
        Guid[] cacheValues = { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var sut = CreateSut();

        for (int i = 0; i < keys.Length; i++)
        {
            var setResult = await sut.SetCacheItemAsync<Guid>(keys[i], cacheValues[i]);
            Assert.True(setResult);
        }


        //Act
        await sut.RemoveItemsFromCacheAsync(keys);

        //Assert
        for (int i = 0; i < keys.Length; i++)
        {
            var getResult = await sut.GetItemsFromCacheAsync<Guid>(keys[i]);
            Assert.Equal(default(Guid), getResult);
        }
    }
}
