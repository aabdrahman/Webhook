using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Services;
using WebHook.Infrastructure.Utilities;
using WebHook.IntegrationTests.BackgroundWorkers;
using static MassTransit.ValidationResultExtensions;

namespace WebHook.Tests.UnitTests.Services;

public class WebhookServiceClientCatalogServiceTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _postgreSqlFixture;
    private readonly Mock<IAuthenticatedUserDetails> _authenticatedUserDetailsMock = null;
    public WebhookServiceClientCatalogServiceTests(PostgreSqlFixture postgreSqlFixture)
    {
        _postgreSqlFixture = postgreSqlFixture;
        _authenticatedUserDetailsMock = new Mock<IAuthenticatedUserDetails>();
    }

    private ServiceProvider _serviceProvider = null;
    private List<WebHookEventCatalog> _eventCatalogs = [];

    public async Task DisposeAsync()
    {
        if (_serviceProvider is not null)
            await _serviceProvider.DisposeAsync();
       
    }

    public async Task InitializeAsync()
    {

        var services = new ServiceCollection();

        services.AddDbContext<RepositoryContext>(opts =>
        {
            opts.UseNpgsql(_postgreSqlFixture.ConnectionString);
        });

        services.AddScoped<IAuthenticatedUserDetails, AuthenticatedUserDetails>();
        services.AddHttpContextAccessor();

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        await ctx.Database.EnsureDeletedAsync();
        await ctx.Database.EnsureCreatedAsync();

        await ctx.AddRangeAsync(GetEventCatalogs());
        await ctx.SaveChangesAsync();

        _eventCatalogs = await ctx.WebHookEventCatalogs.ToListAsync();
    }

    //UTILITY METHODS
    private WebHookEventCatalog BuildCatalogEntity(List<string> availableFields, string name = "CustomerCreated") => new WebHookEventCatalog()
    {
        Id = Guid.NewGuid(),
        EventName = name,
        IsActive = true,
        Description = $"Test Event Catalog: {name}",
        CreatedAt = DateTimeOffset.UtcNow,
        AvailableFields = availableFields.ToDictionary(f => f, f => "string"),
        NormalizedEventName = name.ToUpper()
    };
    private List<WebHookEventCatalog> GetEventCatalogs()
    {
        return
        [
            BuildCatalogEntity(["name", "email"]),
            BuildCatalogEntity(["productId", "orderCount"], "OrderCreated"),
            BuildCatalogEntity(["amount", "refrence"], "PaymentReceived"),
            BuildCatalogEntity(["name", "userid"], "UserCreated")
        ];
    }

    private WebhookServiceClientCatalogService CreateSut(IAuthenticatedUserDetails authenticatedUserDetails = null)
    {
        return new WebhookServiceClientCatalogService(_serviceProvider.GetRequiredService<RepositoryContext>(), authenticatedUserDetails ?? _serviceProvider.GetRequiredService<IAuthenticatedUserDetails>());
    }

    private WebhookServiceClient BuildServiceClientEntity(Guid[] subscribedCatalogs, string clientId = "", string clientKey = "", string clientName = "", string createdBy = "") => new WebhookServiceClient()
    {

        ClientId = clientId,
        ServiceClientName = clientName,
        EventCatalogs = subscribedCatalogs.ToList().Select(x => new WebhookServiceClientEventCatalog() { EventCatalogId = x }).ToList(),
        ClientKey = string.IsNullOrEmpty(clientKey) ? Random.Shared.GetHexString(16) : clientKey,
        CreatedBy = string.IsNullOrEmpty(createdBy) ? Guid.NewGuid().ToString("N") : createdBy
    };

    private WebhookServiceClientEventCatalog BuildClientCatalogEntity(Guid serviceClientId, string catalogName) => new WebhookServiceClientEventCatalog()
    {
        ServiceClientId = serviceClientId,
        EventCatalogId = _eventCatalogs.First(x => x.NormalizedEventName.Contains(catalogName, StringComparison.OrdinalIgnoreCase)).Id
    };

    //------------------
    //GetSubscribedCatalogsAsync
    //------------------
    [Fact]
    public async Task GetSubscribedCatalogsAsync_CancellationTokenRequested_Returns500InternalServerError()
    {
        //Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateSut();

        //Act
        var result = await sut.GetSubscribedCatalogsAsync(Guid.NewGuid(), ct: cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.NotNull(result.ErrorDetail);
        Assert.Null(result.ResponseData);
        Assert.Equal("An error occurred while fetching subscribed catalogs.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task GetSubscribedCatalogsAsync_NoSubscribedCatalog_Returns404NotFound()
    {
        //Arrange
        var catalogIds = _eventCatalogs.Select(x => x.Id).ToList();
        Guid clientId = Guid.NewGuid();
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var clientEntity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service");
            clientEntity.EventCatalogs.Clear();
            await arrangeCtx.WebhookServiceClients.AddAsync(clientEntity);
            await arrangeCtx.SaveChangesAsync();

            clientId = clientEntity.Id;
        }

        var sut = CreateSut();

        //Act
        var result = await sut.GetSubscribedCatalogsAsync(clientId);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Null(result.ResponseData);
        Assert.Null(result.ErrorDetail);
        Assert.StartsWith("No subscribed event catalog for provided id", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(clientId.ToString(), result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSubscribedCatalogsAsync_ClientIdDoesNotExist_Returns404NotFound()
    {
        //Arrange
        var sut = CreateSut();
        Guid randomId = Guid.NewGuid();

        //Act
        var result = await sut.GetSubscribedCatalogsAsync(randomId);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Null(result.ResponseData);
        Assert.Null(result.ErrorDetail);
        Assert.StartsWith("No subscribed event catalog for provided id", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(randomId.ToString(), result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSubscribedCatalogAsync_ClientIdExists_Returns200OK()
    {
        //Arrange
        var catalogIds = _eventCatalogs.Select(x => x.Id).ToList();
        Guid clientId = Guid.NewGuid();
        var subscribedCatalogCount = 0;
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var clientEntity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service");

            await arrangeCtx.WebhookServiceClients.AddAsync(clientEntity);
            await arrangeCtx.SaveChangesAsync();

            clientId = clientEntity.Id;
            subscribedCatalogCount = clientEntity.EventCatalogs.Count;
        }

        var sut = CreateSut();

        //Act
        var result = await sut.GetSubscribedCatalogsAsync(clientId);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.NotNull(result.ResponseData);
        Assert.Null(result.ErrorDetail);
        Assert.Equal("Subscribed event catalogs fetched successfully.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(subscribedCatalogCount, result.ResponseData.Count);
    }

    [Fact]
    public async Task GetSubscribedCatalogAsync_ClientIdExistsCatalogDeactivatedNotIncluded_Returns200OK()
    {
        //Arrange
        var catalogIds = _eventCatalogs.Select(x => x.Id).ToList();
        Guid clientId = Guid.NewGuid();
        var subscribedCatalogCount = 0;
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var clientEntity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(2, catalogIds.Count)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service");
            foreach (var catalog in clientEntity.EventCatalogs)
            {
                catalog.DeactivatedAt = DateTimeOffset.UtcNow.AddSeconds(-5);
                catalog.DeactivatedBy = Guid.NewGuid().ToString("N");
                break;
            }
            await arrangeCtx.WebhookServiceClients.AddAsync(clientEntity);
            await arrangeCtx.SaveChangesAsync();

            clientId = clientEntity.Id;
            subscribedCatalogCount = clientEntity.EventCatalogs.Count(x => !x.DeactivatedAt.HasValue);
        }

        var sut = CreateSut();

        //Act
        var result = await sut.GetSubscribedCatalogsAsync(clientId);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful, result.ResponseMessage);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.NotNull(result.ResponseData);
        Assert.Null(result.ErrorDetail);
        Assert.Equal("Subscribed event catalogs fetched successfully.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(subscribedCatalogCount, result.ResponseData.Count);
    }

    [Fact]
    public async Task GetSubscribedCatalogAsync_ClientIdExistsCatalogIncludeDeactivated_Returns200OK()
    {
        //Arrange
        var catalogIds = _eventCatalogs.Select(x => x.Id).ToList();
        Guid clientId = Guid.NewGuid();
        var subscribedCatalogCount = 0;
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var clientEntity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(2, catalogIds.Count)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service");
            foreach (var catalog in clientEntity.EventCatalogs)
            {
                catalog.DeactivatedAt = DateTimeOffset.UtcNow.AddSeconds(-5);
                catalog.DeactivatedBy = Guid.NewGuid().ToString("N");
                break;
            }
            await arrangeCtx.WebhookServiceClients.AddAsync(clientEntity);
            await arrangeCtx.SaveChangesAsync();

            clientId = clientEntity.Id;
            subscribedCatalogCount = clientEntity.EventCatalogs.Count;
        }

        var sut = CreateSut();

        //Act
        var result = await sut.GetSubscribedCatalogsAsync(clientId, includeDeactivated: true);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.NotNull(result.ResponseData);
        Assert.Null(result.ErrorDetail);
        Assert.Equal("Subscribed event catalogs fetched successfully.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(subscribedCatalogCount, result.ResponseData.Count);
    }

    //-------------------------
    //UnSubscribeFromCatalogAsync
    //-------------------------
    [Fact]
    public async Task UnSubscribeFromCatalogAsync_CancellationRequested_Returns500InternalServerError()
    {
        //Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateSut();

        //Act
        var result = await sut.UnSubscribeFromCatalogAsync(Guid.NewGuid(), "order-created", cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.NotNull(result.ErrorDetail);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("An error occurred while unsubscribing from catalog.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task UnSubscribeFromCatalogAsync_ClientIdExistsCatalogNameNotExists_Returns404NotFound()
    {
        //Arrange
        var catalogIds = _eventCatalogs.Select(x => x.Id).ToList();
        Guid clientId = Guid.NewGuid();
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var clientEntity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(2, catalogIds.Count)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service");

            await arrangeCtx.WebhookServiceClients.AddAsync(clientEntity);
            await arrangeCtx.SaveChangesAsync();

            clientId = clientEntity.Id;
        }

        var sut = CreateSut();

        //Act
        var result = await sut.UnSubscribeFromCatalogAsync(clientId, "acctgenerated");

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Catalog does not exist for provided client.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
    }

    [Fact]
    public async Task UnSubscribeFromCatalogAsync_ClientIdNotExistsCatalogNameExists_Returns404NotFound()
    {
        //Arrange
        var catalogIds = _eventCatalogs.Select(x => x.Id).ToList();
        Guid clientId = Guid.NewGuid(); string selectedName = string.Empty;
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var clientEntity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(2, catalogIds.Count)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service");

            await arrangeCtx.WebhookServiceClients.AddAsync(clientEntity);
            await arrangeCtx.SaveChangesAsync();

            var catalogId = clientEntity.EventCatalogs.OrderBy(x => Guid.NewGuid()).First().EventCatalogId;
            selectedName = _eventCatalogs.First(x => x.Id == catalogId).NormalizedEventName.ToLower();

        }

        var sut = CreateSut();

        //Act
        var result = await sut.UnSubscribeFromCatalogAsync(clientId, selectedName);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Catalog does not exist for provided client.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
    }

    [Fact]
    public async Task UnSubscribeFromCatalogAsync_ValidRequest_Returns200OK()
    {
        //Arrange
        var catalogIds = _eventCatalogs.Select(x => x.Id).ToList();
        Guid clientId = Guid.NewGuid(); string selectedName = string.Empty;
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var clientEntity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(2, catalogIds.Count)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service");

            await arrangeCtx.WebhookServiceClients.AddAsync(clientEntity);
            await arrangeCtx.SaveChangesAsync();

            var catalogId = clientEntity.EventCatalogs.OrderBy(x => Guid.NewGuid()).First().EventCatalogId;
            selectedName = _eventCatalogs.First(x => x.Id == catalogId).NormalizedEventName.ToLower();
            clientId = clientEntity.Id;

        }

        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var sut = CreateSut(authenticatedUserDetails: _authenticatedUserDetailsMock.Object);

        //Act
        var result = await sut.UnSubscribeFromCatalogAsync(clientId, selectedName);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Catalog unsubscribed from succssfully.", result.ResponseMessage, ignoreCase: true);
    }

    //----------------
    //SubscribeToCatalogAsync
    //----------------
    [Fact]
    public async Task SubscribeToCatalogAsync_CancellationRequested_Returns500InternalServerError()
    {
        //Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateSut();

        //Act
        var result = await sut.SubscribeToCatalogAsync(Guid.NewGuid(), "customercreated", cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.NotNull(result.ErrorDetail);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("An error occurred while performing operation.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task SubscribeToCatalogAsync_EventCatalogAlreadySubscribedAndActive_Returns409Conflict()
    {
        //Arrange
        var catalogIds = _eventCatalogs.Select(x => x.Id).ToList();
        Guid clientId = Guid.NewGuid(); string selectedName = string.Empty;
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var clientEntity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service");

            await arrangeCtx.WebhookServiceClients.AddAsync(clientEntity);
            await arrangeCtx.SaveChangesAsync();

            clientId = clientEntity.Id;
            var catalogId = clientEntity.EventCatalogs.OrderBy(x => Guid.NewGuid()).First().EventCatalogId;
            selectedName = _eventCatalogs.First(x => x.Id == catalogId).NormalizedEventName;
        }

        var sut = CreateSut();

        //Act
        var result = await sut.SubscribeToCatalogAsync(clientId, selectedName);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Conflict, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData);
        Assert.StartsWith("Catalog has already been subscribed to", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(selectedName.ToString(), result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubscribeToCatalogAsync_EventCatalogAlreadySubscribedAndInActive_Returns200OK()
    {
        //Arrange
        var catalogIds = _eventCatalogs.Select(x => x.Id).ToList();
        Guid clientId = Guid.NewGuid(); string selectedName = string.Empty;
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var clientEntity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(2, catalogIds.Count)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service");

            foreach (var catalog in clientEntity.EventCatalogs)
            {
                catalog.DeactivatedAt = DateTimeOffset.UtcNow.AddSeconds(-120);
                catalog.DeactivatedBy = Guid.NewGuid().ToString("N");
                break;
            }

            await arrangeCtx.WebhookServiceClients.AddAsync(clientEntity);
            await arrangeCtx.SaveChangesAsync();

            clientId = clientEntity.Id;
            var catalogId = clientEntity.EventCatalogs.Where(x => x.DeactivatedAt.HasValue).OrderBy(x => Guid.NewGuid()).First().EventCatalogId;
            selectedName = _eventCatalogs.First(x => x.Id == catalogId).NormalizedEventName;
        }

        var sut = CreateSut();

        //Act
        var result = await sut.SubscribeToCatalogAsync(clientId, selectedName);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("Operation Successful.", result.ResponseData);
        Assert.Equal("Subscribed event catalog has been reactivated successfully.", result.ResponseMessage, ignoreCase: true);

        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updatedDetails = await assertCtx.WebhookServiceClientEventCatalogs.FirstOrDefaultAsync(x => x.ServiceClientId == clientId && x.eventCatalog.NormalizedEventName == selectedName.ToUpper());
        Assert.NotNull(updatedDetails);
        Assert.Null(updatedDetails.DeactivatedAt);
    }

    [Fact]
    public async Task SubscribeToCatalogAsync_InvalidCatalog_Returns404NotFound()
    {
        //Arrange
        Guid clientId = Guid.NewGuid();
        string catalogName = "customerupdated";

        var sut = CreateSut();

        //Act
        var result = await sut.SubscribeToCatalogAsync(clientId, catalogName);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.StartsWith("Event Catalog to subscribe does not exist", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(catalogName, result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubscribeToCatalogAsync_ClientIdNotExist_Returns404NotFound()
    {
        //Arrange
        string catalogName = _eventCatalogs.OrderBy(x => Guid.NewGuid()).First().NormalizedEventName;
        var sut = CreateSut();
        Guid clientId = Guid.NewGuid();

        //Act
        var result = await sut.SubscribeToCatalogAsync(clientId, catalogName);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.StartsWith("Service client with provided id does not exist:", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(clientId.ToString(), result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubscribeToCatalogAsync_ValidRequest_Returns200OK()
    {
        //Arrange
        var catalogIds = _eventCatalogs.Select(x => x.Id).ToList();
        Guid clientId = Guid.NewGuid(); string selectedName = string.Empty;
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var clientEntity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count-1)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service");

            await arrangeCtx.WebhookServiceClients.AddAsync(clientEntity);
            await arrangeCtx.SaveChangesAsync();

            clientId = clientEntity.Id;
            var notSelectedCatalog = _eventCatalogs.Where(x => !clientEntity.EventCatalogs.Select(x => x.Id).ToList().Contains(x.Id)).Select(x => x.Id).ToList();
            selectedName = _eventCatalogs.Where(x => notSelectedCatalog.Contains(x.Id)).First().NormalizedEventName;
        }

        var sut = CreateSut();

        //Act
        var result = await sut.SubscribeToCatalogAsync(clientId, selectedName);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful, result.ResponseMessage);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("Operation Successful.", result.ResponseData);
        Assert.Equal("Catalog has been subscribed to successfully.", result.ResponseMessage, ignoreCase: true);

        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updatedDetails = await assertCtx.WebhookServiceClientEventCatalogs.FirstOrDefaultAsync(x => x.ServiceClientId == clientId && x.eventCatalog.NormalizedEventName == selectedName.ToUpper());
        Assert.NotNull(updatedDetails);
        Assert.Null(updatedDetails.DeactivatedAt);
    }

    [Fact]
    public async Task SubscribeToCatalogAsync_ValidRequestDeactivatedClient_Returns404NotFound()
    {
        //Arrange
        var catalogIds = _eventCatalogs.Select(x => x.Id).ToList();
        Guid clientId = Guid.NewGuid(); string selectedName = string.Empty;
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var clientEntity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count - 1)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service");
            clientEntity.IsActive = false;
            clientEntity.DeactivatedAt = DateTimeOffset.UtcNow.AddSeconds(-5);
            clientEntity.DeactivatedBy = Guid.NewGuid().ToString("N");
            await arrangeCtx.WebhookServiceClients.AddAsync(clientEntity);
            await arrangeCtx.SaveChangesAsync();

            clientId = clientEntity.Id;
            var notSelectedCatalog = _eventCatalogs.Where(x => !clientEntity.EventCatalogs.Select(x => x.Id).ToList().Contains(x.Id)).Select(x => x.Id).ToList();
            selectedName = _eventCatalogs.Where(x => notSelectedCatalog.Contains(x.Id)).First().NormalizedEventName;
        }

        var sut = CreateSut();

        //Act
        var result = await sut.SubscribeToCatalogAsync(clientId, selectedName);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.StartsWith("Service client with provided id does not exist:", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(clientId.ToString(), result.ResponseMessage, StringComparison.OrdinalIgnoreCase);

    }
}
