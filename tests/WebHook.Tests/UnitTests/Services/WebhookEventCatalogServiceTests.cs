using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using WebHook.Core.DataTransferObjects.WebhookEventCatalog;
using WebHook.Core.Entities;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Services;

namespace WebHook.Tests.UnitTests.Services;

public sealed class WebhookEventCatalogServiceTests : IDisposable
{
    private readonly DbContextOptions<RepositoryContext> _dbContextOptions;
    public WebhookEventCatalogServiceTests()
    {
        _dbContextOptions = new DbContextOptionsBuilder<RepositoryContext>()
                                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                                    .Options;

        Log.Logger = new LoggerConfiguration().CreateLogger();

        using var ctx = new RepositoryContext(_dbContextOptions);
        ctx.Database.EnsureCreated();
    }

    private (RepositoryContext ctx, WebhookEventCatalogService svc) GetSut()
    {
        var context = new RepositoryContext(_dbContextOptions);
        return (context, new WebhookEventCatalogService(context));
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

    private CreateEventCatalogDto BuildCreateCatalogDto(List<string> availableFields, string name = "CustomerCreated") => new()
    {
        Description = $"",
        EventCatalogName = name,
        AvailableFields = availableFields
    };

    [Fact]
    public async Task CreateNewEventCatalogAsync_ValidRequest_Returns201AndPersistsRecord()
    {
        //Arrange
        var createEventDto = BuildCreateCatalogDto(["name", "reference", "amount"], "PaymentCompleted");
        var operationParameters = GetSut();

        //Act
        var result = await operationParameters.svc.CreateNewEventCatalogAsync(createEventDto);
        Console.WriteLine(result);

        //Assert
        Assert.NotNull(result);
        //Assert.True(result.IsSuccessful);
        Assert.True(result.ResponseMessage.Contains("successfully created", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.HttpStatusCode == System.Net.HttpStatusCode.Created);

        var persisted = await operationParameters.ctx.WebHookEventCatalogs.FirstOrDefaultAsync(x => x.EventName == createEventDto.EventCatalogName);

        Assert.NotNull(persisted);
        Assert.True(persisted.EventName.Contains(createEventDto.EventCatalogName, StringComparison.OrdinalIgnoreCase));

    }

    [Fact]
    public async Task CreateNewEventCatalogAsync_DuplicateName_Returns409Conflict()
    {
        //Arrange
        var recordToAdd = BuildCatalogEntity(["name", "email"]);
        var operationParameters = GetSut();
        await operationParameters.ctx.WebHookEventCatalogs.AddAsync(recordToAdd);
        await operationParameters.ctx.SaveChangesAsync();

        var createEventCatalogDto = BuildCreateCatalogDto(["name", "email"]);

        //Act
        var result = await operationParameters.svc.CreateNewEventCatalogAsync(createEventCatalogDto);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.True(result.HttpStatusCode == HttpStatusCode.Conflict);
        Assert.Null(result.ResponseData);
        Assert.True(result?.ResponseMessage.Contains("already exists", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateNewEventCatalogAsync_CancellationRequested_Returns500()
    {
        // Arrange
        var (_, svc) = GetSut();
        var dto = BuildCreateCatalogDto(["name", "balance"],"AccountApproved");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await svc.CreateNewEventCatalogAsync(dto, cts.Token);

        // Assert
        Assert.False(result.IsSuccessful);
        Assert.True(result.HttpStatusCode == HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task EventCatalogActivationAsync_Deactivate_ActiveCatalog_SetIsACtiveFalse()
    {
        //Arrange
        var operationParameters = GetSut();
        var eventCatalogEntity = BuildCatalogEntity(["name"]);

        await operationParameters.ctx.WebHookEventCatalogs.AddAsync(eventCatalogEntity);
        await operationParameters.ctx.SaveChangesAsync();

        //Act
        var result = await operationParameters.svc.EventCatalogActivationAsync(eventCatalogEntity.Id, isDeactivate: true);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.HttpStatusCode == HttpStatusCode.NoContent);
        Assert.True(result.IsSuccessful);

        var modifiedData = await operationParameters.ctx.WebHookEventCatalogs.FirstOrDefaultAsync(x => x.Id == eventCatalogEntity.Id);
        Assert.NotNull(modifiedData);
        Assert.False(modifiedData.IsActive);
    }

    [Fact]
    public async Task EventCatalogActivationAsync_Deactivate_AlreadyInactiveCatalog_SetInactiveFalseAndReturn204()
    {
        //Arrange
        var operationParameters = GetSut();
        var eventCatalogEntity = BuildCatalogEntity(["name"]);
        eventCatalogEntity.IsActive = false;
        await operationParameters.ctx.WebHookEventCatalogs.AddAsync(eventCatalogEntity);
        await operationParameters.ctx.SaveChangesAsync();

        //Act
        var result = await operationParameters.svc.EventCatalogActivationAsync(eventCatalogEntity.Id, isDeactivate: true);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.HttpStatusCode == HttpStatusCode.NoContent);
        Assert.True(result.IsSuccessful);

        var modifiedData = await operationParameters.ctx.WebHookEventCatalogs.FirstOrDefaultAsync(x => x.Id == eventCatalogEntity.Id);
        Assert.NotNull(modifiedData);
        Assert.False(modifiedData.IsActive);

    }

    [Fact]
    public async Task EventCatalogActivationAsync_Deactivate_NonExistingId_Return404()
    {
        //Arrange
        var operationParameters = GetSut();

        //Act
        var result = await operationParameters.svc.EventCatalogActivationAsync(Guid.NewGuid(), isDeactivate: true);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.ResponseMessage.Contains("does not exist", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.HttpStatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EventCatalogActivationAsync_Reactivate_NonExistingId_Returns404()
    {
        //Arrange
        var operationParameters = GetSut();

        //Act
        var result = await operationParameters.svc.EventCatalogActivationAsync(Guid.NewGuid(), isDeactivate: false);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.ResponseMessage.Contains("does not exist", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.HttpStatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EventCatalogActivationAsync_Reactivate_AlreadyInactive_SetActiveAndReturns204()
    {
        //Arrange
        var operationParameters = GetSut();
        var eventCatalogEntity = BuildCatalogEntity(["name"]);
        eventCatalogEntity.IsActive = false;
        await operationParameters.ctx.WebHookEventCatalogs.AddAsync(eventCatalogEntity);
        await operationParameters.ctx.SaveChangesAsync();

        //Act
        var result = await operationParameters.svc.EventCatalogActivationAsync(eventCatalogEntity.Id, isDeactivate: false);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.True(result.HttpStatusCode == HttpStatusCode.NoContent);

        var modified = await operationParameters.ctx.WebHookEventCatalogs.FindAsync(eventCatalogEntity.Id);
        Assert.NotNull(modified);
        Assert.True(modified.IsActive);
        
    }

    [Fact]
    public async Task GetAllEventCatalogAsync_CancellationRequested_Returns500()
    {
        //Arrange
        var operationParameters = GetSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        //Act
        var result = await operationParameters.svc.GetAllEventCatalogAsync(cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.True(result.HttpStatusCode == HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task GetAllEventCatalogAsync_WithRecords_ReturnsAllRecordsAnd200()
    {
        //Arrange
        var operationParameters = GetSut();
        await operationParameters.ctx.WebHookEventCatalogs.AddRangeAsync
        (
            BuildCatalogEntity(["productId", "orderCount"], "ProductOrdered"),
            BuildCatalogEntity(["amount", "reference"], "PaymentRecived"),
            BuildCatalogEntity(["tranRef", "tranAmt"], "PaymentSuccessful")
        );
        await operationParameters.ctx.SaveChangesAsync();

        //Act
        var result = await operationParameters.svc.GetAllEventCatalogAsync();

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.True(result.HttpStatusCode == HttpStatusCode.OK);
        Assert.True(result!.ResponseData!.Any());

    }

    public async Task GetAllEventCatalogAsync_NoRecord_Returns404()
    {
        //Arrange
        var operationParameters = GetSut();

        //Act
        var result = await operationParameters.svc.GetAllEventCatalogAsync();

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.True(result.HttpStatusCode == HttpStatusCode.NotFound);
        Assert.True(!result.ResponseData!.Any());
    }
    
    [Fact]
    public async Task GetAllEventCatalogAsync_WithBothActiveAndInactiveRecords_ReturnsAllRecordsAnd200()
    {
        //Arrange
        var operationParameters = GetSut();
        await operationParameters.ctx.WebHookEventCatalogs.AddRangeAsync
        (
            BuildCatalogEntity(["productId", "orderCount"], "ProductOrdered"),
            BuildCatalogEntity(["amount", "reference"], "PaymentRecived"),
            BuildCatalogEntity(["tranRef", "tranAmt"], "PaymentSuccessful")
        );

        var inactiveRecord = BuildCatalogEntity(["id", "phonenumber"], "UserRegistered");
        inactiveRecord.IsActive = false;

        await operationParameters.ctx.WebHookEventCatalogs.AddAsync(inactiveRecord);
        await operationParameters.ctx.SaveChangesAsync();

        //Act
        var result = await operationParameters.svc.GetAllEventCatalogAsync();

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.True(result.HttpStatusCode == HttpStatusCode.OK);
        Assert.True(result!.ResponseData!.Any());
        Assert.True(result!.ResponseData!.Count() == 4);
    }

    [Fact]
    public async Task GetEventCatalogByIdAsync_NonExistent_Returns404()
    {
        //Arrange
        var operationParameters = GetSut();

        //Act
        var result = await operationParameters.svc.GetEventCatalogByIdAsync(Guid.NewGuid());

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.True(result.HttpStatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetEventCatalogByIdAsync_ExistingId_Returns200()
    {
        //Arrange
        var operationParameters = GetSut();
        var catalogEntity = BuildCatalogEntity(["name"]);
        await operationParameters.ctx.WebHookEventCatalogs.AddAsync(catalogEntity);
        await operationParameters.ctx.SaveChangesAsync();

        //Act
        var result = await operationParameters.svc.GetEventCatalogByIdAsync(catalogEntity.Id);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.True(result.HttpStatusCode == HttpStatusCode.OK);
        Assert.True(result.ResponseData.EventCatalogName.Contains(catalogEntity.EventName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetEventCatalogByIdAsync_ReturnsCorrectCatalogFromMultiple()
    {
        //Arrange
        var operationParameters = GetSut();
        var targetCatalog = BuildCatalogEntity(["phonenumber"], "UserCreated");
        List<WebHookEventCatalog> catalogs = new List<WebHookEventCatalog>()
        {
            BuildCatalogEntity(["name"]),
            BuildCatalogEntity(["id"], "PaymentReceived"),
            targetCatalog
        };

        await operationParameters.ctx.WebHookEventCatalogs.AddRangeAsync(catalogs);
        await operationParameters.ctx.SaveChangesAsync();

        //Act
        var result = await operationParameters.svc.GetEventCatalogByIdAsync(targetCatalog.Id);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.True(result.HttpStatusCode == HttpStatusCode.OK);
        Assert.True(result.ResponseData.EventCatalogName.Contains(targetCatalog.EventName, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        using var ctx = new RepositoryContext(_dbContextOptions);
        ctx.Database.EnsureDeleted();
    }
}
