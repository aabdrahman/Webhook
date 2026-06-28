using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects.WebhookSubscription;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Services;

namespace WebHook.Tests.UnitTests.Services;

public class WebhookSubscriptionTests : IDisposable
{
    private readonly DbContextOptions<RepositoryContext> _dbContextOptions;
    private readonly Mock<IOptionsMonitor<SignatureSecretConfiguration>> _signatureSecretConfigurationMocker;
    private readonly Mock<ISecretKeyGenerator> _secretGeneratorMock;
    private readonly Mock<IEncryptionService> _encryptionServiceMock;

    public WebhookSubscriptionTests()
    {

        _dbContextOptions = new DbContextOptionsBuilder<RepositoryContext>()
                                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                                    .Options;

        Log.Logger = new LoggerConfiguration().CreateLogger();

        //Secret Configuration Mocking
        _signatureSecretConfigurationMocker = new Mock<IOptionsMonitor<SignatureSecretConfiguration>>();
        _signatureSecretConfigurationMocker.Setup(ssc =>  ssc.CurrentValue).Returns(new SignatureSecretConfiguration() { KeySize = 32 });

        //Secret Key Generator Mocker
        _secretGeneratorMock = new Mock<ISecretKeyGenerator>();
        _secretGeneratorMock.Setup(sg => sg.GenerateKey(It.IsAny<int>())).Returns("my-secret");

        //Encryption Service Mocker
        _encryptionServiceMock = new Mock<IEncryptionService>();
        _encryptionServiceMock.Setup(es => es.Encrypt(It.IsAny<string>())).Returns("my-encrypted-secret");

        //Databse creation at test initializer.
        using var ctx = new RepositoryContext(_dbContextOptions);
        ctx.Database.EnsureCreated();
        ctx.WebHookEventCatalogs.AddRange(GetEventCatalogs());
        ctx.SaveChanges();
    }

    private (RepositoryContext ctx, WebhookSubscriptionService svc) GetSut()
    {
        var context = new RepositoryContext(_dbContextOptions);
       return (context, new WebhookSubscriptionService(context, _secretGeneratorMock.Object, _signatureSecretConfigurationMocker.Object, _encryptionServiceMock.Object));
    }

    private WebHookEventCatalog BuildCatalogEntity(List<string> availableFields, string name = "CustomerCreated") => new WebHookEventCatalog()
    {
        Id = Guid.NewGuid(),
        EventName = name,
        IsActive = true,
        Description = $"Test Event Catalog: {name}",
        CreatedAt = DateTimeOffset.UtcNow,
        AvailableFields = availableFields,
        NormalizedEventName = name.ToUpper()
    };

    private WebhookSubscription BuildEntity(string entityName, List<Guid> eventIds)
    {
        var entityId = Guid.NewGuid();

        return new WebhookSubscription()
        {
            Id = entityId,
            Name = entityName,
            IsActive = true,
            SubscribedFields = [],
            CallbackUrl = "https://example.com/",
            SecretKey = Random.Shared.GetHexString(32),
            WebhookEvents = eventIds.Select(x => new WebhookSubscriptionEvent() { WebhookSubscriptionId = entityId, WebhookEventCatalogId = x }).ToList()
        };
    }

    private CreateWebhookSubscriptionDto BuildCreateDto()
    {
        return new CreateWebhookSubscriptionDto()
        {
            CallBackUrl = "https://example.com/",
            SubscriberName = "User 4",
            SubscribedFields = ["name"],
            SubscribedEvents = ["OrderCreated", "UserCreated"]
        };
    }

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

    public void Dispose()
    {
        using var ctx = new RepositoryContext(_dbContextOptions);
        ctx.Database.EnsureDeleted();
    }

    [Fact]
    public async Task GetAllWebhookSubscriptionAsync_NoWebhookSubscription_Returns404NotFound()
    {
        //Arrange
        var sut = GetSut();

        //Act
        var result = await sut.svc.GetAllWebhookSubscriptionAsync();

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Null(result.ErrorDetail);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
    }

    [Fact]
    public async Task GetAllWebhookSubscriptionAsync_WebhookSubscriptionExists_Returns200OK()
    {
        //Arrange
        var sut = GetSut();
        var existingEventCatalogs = await sut.ctx.WebHookEventCatalogs.ToListAsync();
        List<WebhookSubscription> webhookSubscriptions = 
            [
                BuildEntity("user 1", existingEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, 4)).Select(x => x.Id).ToList()),
                BuildEntity("user 2", existingEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, 4)).Select(x => x.Id).ToList()),
                BuildEntity("user 3", existingEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, 4)).Select(x => x.Id).ToList())
            ];

        await sut.ctx.WebhookSubscriptions.AddRangeAsync(webhookSubscriptions);
        await sut.ctx.SaveChangesAsync();

        //Act
        var result = await sut.svc.GetAllWebhookSubscriptionAsync();

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ResponseData);
        Assert.True(result.IsSuccessful);
        Assert.Equal(result.ResponseData.Count, webhookSubscriptions.Count);
        Assert.Null(result.ErrorDetail);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
    }

    [Fact]
    public async Task GetAllWebhookSubscriptionAsync_CancellationRequested_Returns500()
    {
        //Arrange
        var sut = GetSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        //Act
        var result = await sut.svc.GetAllWebhookSubscriptionAsync(cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.NotNull(result.ErrorDetail);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
    }

    [Fact]
    public async Task GetWebhookSubscriptionByIdAsync_ExistingId_Returns200OK()
    {
        //Arrange
        var sut = GetSut();
        var existingEventCatalogs = await sut.ctx.WebHookEventCatalogs.ToListAsync();
        Guid entityId = Guid.NewGuid();
        var webhookSubscriptionToInsert = BuildEntity("user 1", existingEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, 4)).Select(x => x.Id).ToList());
        webhookSubscriptionToInsert.Id = entityId;
        await sut.ctx.WebhookSubscriptions.AddAsync(webhookSubscriptionToInsert);
        await sut.ctx.SaveChangesAsync();

        //Act
        var result = await sut.svc.GetWebhookSubscriptionByIdAsync(entityId);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
    }

    [Fact]
    public async Task GetWebhookSubscriptionByIdAsync_NonExistingId_Returns404NotFound()
    {
        //Arrange
        var sut = GetSut();


        //Act
        var result = await sut.svc.GetWebhookSubscriptionByIdAsync(Guid.NewGuid());

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal((int)HttpStatusCode.NotFound, (int)result.HttpStatusCode);
    }

    [Fact]
    public async Task GetWebhookSubscriptionByIdAsync_CancellationRequested_ThrowsException_Returns500InternalServerError()
    {
        //Arrange
        var sut = GetSut();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        //Act
        var result = await sut.svc.GetWebhookSubscriptionByIdAsync(Guid.NewGuid(), cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.ErrorDetail);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
    }

    [Fact]
    public async Task DeleteWebhookSubscriptionAsync_CancellationRequested_ThrowsException_Returns500InternalServerError()
    {
        //Arrange
        var sut = GetSut();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        //Act
        var result = await sut.svc.DeleteWebhookSubscriptionAsync(Guid.NewGuid(), cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.ErrorDetail);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
    }

    [Fact]
    public async Task DeleteWebhookSubscriptionAsync_NotExistingId_Returns404Notfound()
    {
        //Arrange
        var sut = GetSut();


        //Act
        var result = await sut.svc.DeleteWebhookSubscriptionAsync(Guid.NewGuid());

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
    }

    [Fact]
    public async Task DeleteWebhookSubscriptionAsync_ExistingId_Returns200Ok()
    {
        //Arrange
        var sut = GetSut();
        var existingEventCatalogs = await sut.ctx.WebHookEventCatalogs.ToListAsync();
        Guid entityId = Guid.NewGuid();
        var webhookSubscriptionToInsert = BuildEntity("user 1", existingEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, 4)).Select(x => x.Id).ToList());
        webhookSubscriptionToInsert.Id = entityId;
        await sut.ctx.WebhookSubscriptions.AddAsync(webhookSubscriptionToInsert);
        await sut.ctx.SaveChangesAsync();


        //Act
        var deleteResult = await sut.svc.DeleteWebhookSubscriptionAsync(entityId);
        var fetchRecordResult = await sut.svc.GetWebhookSubscriptionByIdAsync(entityId);

        //Assert
        Assert.NotNull(deleteResult);
        Assert.True(deleteResult.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, deleteResult.HttpStatusCode);

        Assert.False(fetchRecordResult.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, fetchRecordResult.HttpStatusCode);
    }

    [Fact]
    public async Task ActivateWebhookSubscriptionAsync_CancellationRequested_ThrowsException_Returns500InternalServerError()
    {
        //Arrange
        var sut = GetSut();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        //Act
        var result = await sut.svc.ActivateWebhookSubscriptionAsync(Guid.NewGuid(), cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.ErrorDetail);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
    }

    [Fact]
    public async Task ActivateWebhookSubscriptionAsync_NotExisitingId_Returns404NotFound()
    {
        //Arrange
        var sut = GetSut();

        //Act
        var result = await sut.svc.ActivateWebhookSubscriptionAsync(Guid.NewGuid());

        //Assert
        Assert.NotNull(result);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.False(result.IsSuccessful);
    }

    [Fact]
    public async Task ActivateWebhookSubscriptionAsync_AlreadyActive_Returns409Conflict()
    {
        //Arrange
        var sut = GetSut();
        var existingEventCatalogs = await sut.ctx.WebHookEventCatalogs.ToListAsync();
        Guid entityId = Guid.NewGuid();
        var webhookSubscriptionToInsert = BuildEntity("user 1", existingEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, 4)).Select(x => x.Id).ToList());
        webhookSubscriptionToInsert.Id = entityId;
        await sut.ctx.WebhookSubscriptions.AddAsync(webhookSubscriptionToInsert);
        await sut.ctx.SaveChangesAsync();

        //Act
        var result = await sut.svc.ActivateWebhookSubscriptionAsync(entityId);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Conflict, result.HttpStatusCode);
    }

    [Fact]
    public async Task ActivateWebhookSubscriptionAsync_AlreadyInActive_Returns200OK()
    {
        //Arrange
        var sut = GetSut();
        var existingEventCatalogs = await sut.ctx.WebHookEventCatalogs.ToListAsync();
        Guid entityId = Guid.NewGuid();
        var webhookSubscriptionToInsert = BuildEntity("user 1", existingEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, 4)).Select(x => x.Id).ToList());
        webhookSubscriptionToInsert.Id = entityId;
        webhookSubscriptionToInsert.IsActive = false;
        await sut.ctx.WebhookSubscriptions.AddAsync(webhookSubscriptionToInsert);
        await sut.ctx.SaveChangesAsync();

        //Act
        var result = await sut.svc.ActivateWebhookSubscriptionAsync(entityId);
        var fetchBackResult = await sut.svc.GetWebhookSubscriptionByIdAsync(entityId);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);

        Assert.NotNull(fetchBackResult);
        Assert.True(fetchBackResult.IsSuccessful);
        Assert.NotNull(fetchBackResult.ResponseData);
        Assert.Equal(entityId, fetchBackResult.ResponseData.Id);
    }

    [Fact]
    public async Task CreateWebhookSubscriptionAsync_CancellationRequested_ThrowsException_Returns500InternalServerError()
    {
        //Arrange
        var sut = GetSut();
        var createEntity = BuildCreateDto();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        //Act
        var result = await sut.svc.CreateWebhookSubscriptionAsync(createEntity, cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ErrorDetail);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
    }

    [Fact]
    public async Task CreateWebhookSubscriptionAsync_ValidRequest_Returns201Created()
    {
        //Arrange
        var sut = GetSut();
        var createEntity = BuildCreateDto();

        //Act
        var result = await sut.svc.CreateWebhookSubscriptionAsync(createEntity);
        var fetchResult = await sut.svc.GetAllWebhookSubscriptionAsync();

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Created, result.HttpStatusCode);

        Assert.NotNull(fetchResult);
        Assert.NotNull(fetchResult.ResponseData);
        Assert.True(fetchResult.ResponseData.Any(x => x.Name == createEntity.SubscriberName));
    }

    [Fact]
    public async Task CreateWebhookSubscriptionAsync_InValidRequest_Returns400BadRequest()
    {
        //Arrange
        var sut = GetSut();
        var createEntity = BuildCreateDto();
        createEntity.SubscribedEvents.Add("EventAdded");

        //Act
        var result = await sut.svc.CreateWebhookSubscriptionAsync(createEntity);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
    }
}
