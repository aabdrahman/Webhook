using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Net;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Security;
using WebHook.Infrastructure.Services;
using WebHook.Infrastructure.Utilities;
using WebHook.IntegrationTests.BackgroundWorkers;

namespace WebHook.Tests.UnitTests.Services;

public class WebhookServiceClientServiceTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _postgreSqlFixture;
    private readonly Mock<IAuthenticatedUserDetails> _authenticatedUserDetailsMock = null;
    private readonly Mock<IApplicationHasher> _applicationHasherMock = null;

    public WebhookServiceClientServiceTests(PostgreSqlFixture postgreSqlFixture)
    {
        _postgreSqlFixture = postgreSqlFixture;
        _authenticatedUserDetailsMock = new Mock<IAuthenticatedUserDetails>();
        _applicationHasherMock = new Mock<IApplicationHasher>();
    }
    private ServiceProvider _serviceProvider = null;
    private List<WebHookEventCatalog> _createdEventCatalogs = [];

    public async Task DisposeAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }
    }

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        services.AddDbContext<RepositoryContext>(opts =>
        {
            opts.UseNpgsql(_postgreSqlFixture.ConnectionString);
        });

        services.AddScoped<IWebhookServiceClientService, WebhookServiceClientService>();
        services.AddScoped<IApplicationHasher, ApplicationHasher>();
        services.AddScoped<ICacheService, CacheService>();
        services.AddMemoryCache(opts =>
        {
            opts.SizeLimit = 2 * 1024 * 1024;

        });

        _serviceProvider = services.BuildServiceProvider();

        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        await ctx.Database.EnsureDeletedAsync();
        await ctx.Database.EnsureCreatedAsync();

        
        await ctx.AddRangeAsync(GetEventCatalogs());
        await ctx.SaveChangesAsync();

        _createdEventCatalogs = await ctx.WebHookEventCatalogs.ToListAsync();

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

    private WebhookServiceClientService CreateSut(IApplicationHasher applicationHasher = null, IAuthenticatedUserDetails authenticatedUserDetails = null)
    {
        var scope = _serviceProvider.CreateScope();
        return new WebhookServiceClientService(scope.ServiceProvider.GetRequiredService<RepositoryContext>(), cacheService: scope.ServiceProvider.GetRequiredService<ICacheService>(),
            applicationHasher: applicationHasher ?? _serviceProvider.GetRequiredService<IApplicationHasher>(), authenticatedUserDetails: authenticatedUserDetails ?? _authenticatedUserDetailsMock.Object);
        
    }

    private RequestNewClientKeyDto BuildRequestNewClientKey(string clientId = null, string clientName = "Test Service") => new RequestNewClientKeyDto()
    {
        ClientId = clientId ?? Random.Shared.GetHexString(6),
        ServiceName = clientName
    };

    private CreateServiceClientDto BuildCreateServiceClient(string[] catalogToSubscribe, string clientId = "", string serviceName = "") => new CreateServiceClientDto()
    {
        ContactEmail = "user@example.com",
        ServiceName = serviceName,
        ClientId = clientId,
        AllowedEventTypes = catalogToSubscribe.ToList()
    };

    private WebhookServiceClient BuildServiceClientEntity(Guid[] subscribedCatalogs, string clientId = "", string clientKey = "", string clientName = "", string createdBy = "") => new WebhookServiceClient()
    {

        ClientId = clientId,
        ServiceClientName = clientName,
        EventCatalogs = subscribedCatalogs.ToList().Select(x => new WebhookServiceClientEventCatalog() { EventCatalogId = x }).ToList(),
        ClientKey = string.IsNullOrEmpty(clientKey) ? Random.Shared.GetHexString(16) : clientKey,
        CreatedBy = string.IsNullOrEmpty(createdBy) ? Guid.NewGuid().ToString("N") : createdBy
    };

    [Fact]
    public async Task GetAllClientsAsync_NoClientExists_Returns404NotFound()
    {
        //Arrange
        var sut = CreateSut();

        //Act
        var result = await sut.GetAllClientsAsync();

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Null(result.ResponseData);
        Assert.Equal("No client has been onboarded yet.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task GetAllClientsAsync_CancellationReqeusted_Returns500InternalServer()
    {
        //Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateSut();

        //Act
        var result = await sut.GetAllClientsAsync(ct: cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.Null(result.ResponseData);
        Assert.NotNull(result.ErrorDetail);
        Assert.Equal("An error occurred while getting onboarded clients.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task GetAllClientsAsync_ClientsOnboarded_Returns200OK()
    {
        //Arrange
        List<Guid> catalogIds = _createdEventCatalogs.Select(x => x.Id).ToList();
        int recordCount = 0;
        using (var arrangeScope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = arrangeScope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var clients = new List<WebhookServiceClient>()
            {
                BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "order-service-prod", clientName: "Order Service"),
                BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service"),
            };
            await arrangeCtx.WebhookServiceClients.AddRangeAsync(clients);
            await arrangeCtx.SaveChangesAsync();
            recordCount = clients.Count;
        }

        var sut = CreateSut();

        //Act
        var result = await sut.GetAllClientsAsync();

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("Onboarded clients fetched successfully.", result.ResponseMessage, ignoreCase: true);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(recordCount, result.ResponseData.Count);
    }

    [Fact]
    public async Task GetAllClientsAsync_IncludeDeactivated_Returns200OK()
    {
        //Arrange
        List<Guid> catalogIds = _createdEventCatalogs.Select(x => x.Id).ToList();
        int recordCount = 0;
        using (var arrangeScope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = arrangeScope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var clients = new List<WebhookServiceClient>()
            {
                BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "order-service-prod", clientName: "Order Service"),
                BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service")
            };
            var deactivatedClient = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service");
            deactivatedClient.IsActive = false;
            deactivatedClient.DeactivatedAt = DateTimeOffset.UtcNow.AddSeconds(-90);
            deactivatedClient.DeactivatedBy = Guid.NewGuid().ToString("N");
            clients.Add(deactivatedClient);
            await arrangeCtx.WebhookServiceClients.AddRangeAsync(clients);
            await arrangeCtx.SaveChangesAsync();
            recordCount = clients.Count;
        }

        var sut = CreateSut();

        //Act
        var result = await sut.GetAllClientsAsync(includeDeactivated: true);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("Onboarded clients fetched successfully.", result.ResponseMessage, ignoreCase: true);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(recordCount, result.ResponseData.Count);
    }

    [Fact]
    public async Task GetAllClientsAsync_DeactivatedNotIncluded_Returns200OK()
    {
        //Arrange
        List<Guid> catalogIds = _createdEventCatalogs.Select(x => x.Id).ToList();
        int recordCount = 0;
        using (var arrangeScope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = arrangeScope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var clients = new List<WebhookServiceClient>()
            {
                BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "order-service-prod", clientName: "Order Service"),
                BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service")
            };
            var deactivatedClient = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service");
            deactivatedClient.IsActive = false;
            deactivatedClient.DeactivatedAt = DateTimeOffset.UtcNow.AddSeconds(-90);
            deactivatedClient.DeactivatedBy = Guid.NewGuid().ToString("N");
            clients.Add(deactivatedClient);
            await arrangeCtx.WebhookServiceClients.AddRangeAsync(clients);
            await arrangeCtx.SaveChangesAsync();
            recordCount = clients.Count(x => x.IsActive);
        }

        var sut = CreateSut();

        //Act
        var result = await sut.GetAllClientsAsync(includeDeactivated: false);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("Onboarded clients fetched successfully.", result.ResponseMessage, ignoreCase: true);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(recordCount, result.ResponseData.Count);
    }

    //----------------------
    //GetByClientIdAsync
    //----------------------
    [Fact]
    public async Task GetByClientIdAsync_CancellationRequested_Returns500InternalServerError()
    {
        //Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateSut();

        //Act
        var result = await sut.GetByClientIdAsync(Random.Shared.GetHexString(6), cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.Null(result.ResponseData);
        Assert.NotNull(result.ErrorDetail);
        Assert.Equal("An error occurred whule getting onboarded client details.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task GetByClientIdAsync_ClientIdExists_Returns404NotFound()
    {
        //Arrange
        var catalogIds = _createdEventCatalogs.Select(x => x.Id).ToList();
        var clientId = string.Empty;
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var clientEntity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service");

            await arrangeCtx.WebhookServiceClients.AddAsync(clientEntity);
            await arrangeCtx.SaveChangesAsync();

            clientId = clientEntity.ClientId;
        }

        var sut = CreateSut();

        //Act
        var result = await sut.GetByClientIdAsync(clientId);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(result.ResponseData.ClientId, clientId);
    }

    [Fact]
    public async Task GetByClientIdAsync_DeactivatedClient_Returns404NotFound()
    {
        //Arrange
        var catalogIds = _createdEventCatalogs.Select(x => x.Id).ToList();
        var clientId = string.Empty;
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var clientEntity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service");
            clientEntity.IsActive = false;
            clientEntity.DeactivatedAt = DateTimeOffset.UtcNow.AddSeconds(-120);
            clientEntity.DeactivatedBy = Guid.NewGuid().ToString("N");
            await arrangeCtx.WebhookServiceClients.AddAsync(clientEntity);
            await arrangeCtx.SaveChangesAsync();

            clientId = clientEntity.ClientId;
        }

        var sut = CreateSut();

        //Act
        var result = await sut.GetByClientIdAsync(clientId);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.StartsWith("Service client with provided id does not exist or has been deactivated ", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(clientId, result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetByClientIdAsync_ClientIdNotExists_Returns404NotFound()
    {
        //Arrange
        var catalogIds = _createdEventCatalogs.Select(x => x.Id).ToList();
        var clientId = Random.Shared.GetHexString(6);
        

        var sut = CreateSut();

        //Act
        var result = await sut.GetByClientIdAsync(clientId);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.StartsWith("Service client with provided id does not exist or has been deactivated ", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(clientId, result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    //--------------------------
    //DeactivateClientAsync
    //--------------------------
    [Fact]
    public async Task DeactivateClientAsync_UserIdNotExists_Returns404NotFound()
    {
        //Arrange
        string clientId = Random.Shared.GetHexString(6);
        _authenticatedUserDetailsMock.Setup(x => x.emailAddress).Returns(Random.Shared.GetHexString(6));
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var sut = CreateSut(authenticatedUserDetails: _authenticatedUserDetailsMock.Object);

        //Act
        var result = await sut.DeactivateClientAsync(clientId);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Forbidden, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Invalid Credentials.", result.ResponseMessage,ignoreCase: true);
    }

    [Fact]
    public async Task DeactivateClientAsync_CancellationTokenRequested_Returns500InternalServerError()
    {
        //Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateSut();

        //Act
        var result = await sut.DeactivateClientAsync(Random.Shared.GetHexString(6), cts.Token);


        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.NotNull(result.ErrorDetail);
        Assert.Equal("An error occurred while deactivating service client.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
    }

    [Fact]
    public async Task DeactivateClientAsync_ClientIdNotExists_Returns200OK()
    {
        //Arrange
        Guid userId = Guid.NewGuid();
        string userEmail = string.Empty;
        string clientId = Random.Shared.GetHexString(6);
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            User userToCreate = new User()
            {
                UserName = "TestUser112",
                NormalizedUserName = "TestUser112".ToUpper(),
                Email = "test@example.com",
                NormalizedEmail = "test@example.com".ToUpper(),
                FirstName = "John",
                LastName = "Doe",
                IsActive = true
            };


            await arrangeCtx.Users.AddAsync(userToCreate);
            await arrangeCtx.SaveChangesAsync();
            userId = userToCreate.Id; userEmail = userToCreate.NormalizedEmail;
            clientId = Random.Shared.GetHexString(6);
        }

        _authenticatedUserDetailsMock.Setup(x => x.emailAddress).Returns(userEmail);
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(userId.ToString("N"));
        var sut = CreateSut(authenticatedUserDetails: _authenticatedUserDetailsMock.Object);

        //Act
        var result = await sut.DeactivateClientAsync(clientId);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.StartsWith("Service client with provided id does not exist", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(clientId, result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeactivateClientAsync_ClientIdExists_Returns202oContent()
    {
        //Arrange
        var catalogIds = _createdEventCatalogs.Select(x => x.Id).ToList();
        Guid userId = Guid.NewGuid();
        string userEmail = string.Empty;
        string clientId = Random.Shared.GetHexString(6);
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            User userToCreate = new User()
            {
                Id = Guid.NewGuid(),
                UserName = "TestUser112",
                NormalizedUserName = "TestUser112".ToUpper(),
                Email = "test@example.com",
                NormalizedEmail = "test@example.com".ToUpper(),
                FirstName = "John",
                LastName = "Doe",
                IsActive = true
            };

            var entity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "order-service-prod", clientName: "Order Service", createdBy: userToCreate.Id.ToString("N"));

            await arrangeCtx.WebhookServiceClients.AddAsync(entity);
            await arrangeCtx.Users.AddAsync(userToCreate);
            await arrangeCtx.SaveChangesAsync();
            userId = userToCreate.Id; userEmail = userToCreate.NormalizedEmail;
            clientId = entity.ClientId;
        }

        _authenticatedUserDetailsMock.Setup(x => x.emailAddress).Returns(userEmail);
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(userId.ToString("N"));
        var sut = CreateSut(authenticatedUserDetails: _authenticatedUserDetailsMock.Object);

        //Act
        var result = await sut.DeactivateClientAsync(clientId);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NoContent, result.HttpStatusCode);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Service client successfully deactivated.", result.ResponseMessage, ignoreCase: true);


        var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var updatedEntity = await assertCtx.WebhookServiceClients.IgnoreQueryFilters().Include(x => x.EventCatalogs).FirstOrDefaultAsync(x => x.ClientId == clientId.ToLower());
        Assert.NotNull(updatedEntity);
        Assert.False(updatedEntity.IsActive);
        Assert.NotNull(updatedEntity.DeactivatedAt);
        Assert.NotNull(updatedEntity.DeactivatedBy);
        Assert.Equal(updatedEntity.EventCatalogs.Count, updatedEntity.EventCatalogs.Count(x => x.DeactivatedAt.HasValue));
    }

    [Fact]
    public async Task DeactivateClientAsync_ClientIdExistsButDeactivated_Returns404NotFound()
    {
        //Arrange
        var catalogIds = _createdEventCatalogs.Select(x => x.Id).ToList();
        Guid userId = Guid.NewGuid();
        string userEmail = string.Empty;
        string clientId = Random.Shared.GetHexString(6);
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            User userToCreate = new User()
            {
                Id = Guid.NewGuid(),
                UserName = "TestUser112",
                NormalizedUserName = "TestUser112".ToUpper(),
                Email = "test@example.com",
                NormalizedEmail = "test@example.com".ToUpper(),
                FirstName = "John",
                LastName = "Doe",
                IsActive = true
            };

            var entity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "order-service-prod", clientName: "Order Service", createdBy: userToCreate.Id.ToString("N"));
            entity.IsActive = false;
            entity.DeactivatedAt = DateTimeOffset.UtcNow;
            entity.DeactivatedBy = Random.Shared.GetHexString(6);

            await arrangeCtx.WebhookServiceClients.AddAsync(entity);
            await arrangeCtx.Users.AddAsync(userToCreate);
            await arrangeCtx.SaveChangesAsync();
            userId = userToCreate.Id; userEmail = userToCreate.NormalizedEmail;
            clientId = entity.ClientId;
        }

        _authenticatedUserDetailsMock.Setup(x => x.emailAddress).Returns(userEmail);
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(userId.ToString("N"));
        var sut = CreateSut(authenticatedUserDetails: _authenticatedUserDetailsMock.Object);

        //Act
        var result = await sut.DeactivateClientAsync(clientId);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.StartsWith("Service client with provided id does not exist", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(clientId, result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    //-----------------
    //ReactivateClientAsync
    //-----------------
    [Fact]
    public async Task ReactivateClientAsync_CancellationReqeusted_Returns500InternalServerError()
    {
        //Arrange
        using var ctx = new CancellationTokenSource();
        ctx.Cancel();

        var sut = CreateSut();

        //Act
        var result = await sut.ReactivateClientAsync(Random.Shared.GetHexString(6), ctx.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("An error occurred while reactivating onboarded client.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task ReactivateClientAsync_ClientIdNotExist_Returns404NotFound()
    {
        //Arrange
        string clientId = Random.Shared.GetHexString(6);

        var sut = CreateSut();

        //Act
        var result = await sut.ReactivateClientAsync(clientId);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.StartsWith("Service client with provided id does not exist", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(clientId, result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReactivateClientAsync_ClientAlreadyActive_Retuns409Conflict()
    {
        //Arrange
        var catalogIds = _createdEventCatalogs.Select(x => x.Id).ToList();
        string clientId = string.Empty;
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

            var clientEntity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "order-service-prod", clientName: "Order Service");
            await arrangeCtx.WebhookServiceClients.AddAsync(clientEntity);
            await arrangeCtx.SaveChangesAsync();

            clientId = clientEntity.ClientId;
        }

        var sut = CreateSut();

        //Act
        var result = await sut.ReactivateClientAsync(clientId);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Conflict, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Service client is already active.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task ReactivateClientAsync_ClientInActive_Retuns200OK()
    {
        //Arrange
        var catalogIds = _createdEventCatalogs.Select(x => x.Id).ToList();
        string clientId = string.Empty;
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

            var clientEntity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "order-service-prod", clientName: "Order Service");
            clientEntity.IsActive = false;
            clientEntity.DeactivatedAt = DateTimeOffset.UtcNow.AddSeconds(-150);
            clientEntity.DeactivatedBy = Guid.NewGuid().ToString("N");
            await arrangeCtx.WebhookServiceClients.AddAsync(clientEntity);
            await arrangeCtx.SaveChangesAsync();
            clientId = clientEntity.ClientId;
        }

        var sut = CreateSut();

        //Act
        var result = await sut.ReactivateClientAsync(clientId);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Onboarded client reactivated successfully.", result.ResponseMessage, ignoreCase: true);

        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updatedDetails = await assertCtx.WebhookServiceClients.FirstOrDefaultAsync(x => x.ClientId == clientId);
        Assert.NotNull(updatedDetails);
        Assert.True(updatedDetails.IsActive);
        Assert.NotNull(updatedDetails.DeactivatedAt);
        Assert.NotNull(updatedDetails.DeactivatedBy);
    }

    //-----------------
    //RequestNewClientKeyAsync
    //-----------------
    [Fact]
    public async Task RequestNewClientKeyAsync_CancellationRequested_Returns500InternalServerError()
    {
        //Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var request = BuildRequestNewClientKey(clientId: "test-cient");
        var sut = CreateSut();

        //Act
        var result = await sut.RequestNewClientKeyAsync(request, cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.Null(result.ResponseData);
        Assert.Equal("An error occurred while performing operation.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task RequestNewClientKeyAsync_ClientIdNotExists_Returns404NotFound()
    {
        //Arrange
        var request = BuildRequestNewClientKey(clientId: "test-cient");
        var sut = CreateSut();

        //Act
        var result = await sut.RequestNewClientKeyAsync(request);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Null(result.ResponseData);
        Assert.StartsWith("Service client does not exist:", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(request.ClientId, result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestNewClientKeyAsync_ValidRequest_Returns200OK()
    {
        //Arrange
        var catalogIds = _createdEventCatalogs.Select(x => x.Id).ToList();
        string clientId = string.Empty; string hashedSecret = "hashed-secret-client-key";
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

            var clientEntity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "order-service-prod", clientName: "Order Service");
            await arrangeCtx.WebhookServiceClients.AddAsync(clientEntity);
            await arrangeCtx.SaveChangesAsync();

            clientId = clientEntity.ClientId;
        }

        _applicationHasherMock.Setup(x => x.HashSecret(It.IsAny<string>())).ReturnsAsync(hashedSecret);
        var request = BuildRequestNewClientKey(clientId: clientId);
        var sut = CreateSut(applicationHasher: _applicationHasherMock.Object);

        //Act
        var result = await sut.RequestNewClientKeyAsync(request);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.NotNull(result.ResponseData);

        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var updatedDetails = await assertCtx.WebhookServiceClients.FirstOrDefaultAsync(x => x.ClientId == clientId);
        Assert.NotNull(updatedDetails);
        Assert.Equal(updatedDetails.ClientKey, hashedSecret);
    }

    [Fact]
    public async Task RequestNewClientKeyAsync_DeactivatedCient_Returns404NotFound()
    {
        //Arrange
        var catalogIds = _createdEventCatalogs.Select(x => x.Id).ToList();
        string clientId = string.Empty; string hashedSecret = "hashed-secret-client-key";
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

            var clientEntity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "order-service-prod", clientName: "Order Service");
            clientEntity.IsActive = false;
            clientEntity.DeactivatedAt = DateTimeOffset.UtcNow.AddSeconds(-120);
            clientEntity.DeactivatedBy = Guid.NewGuid().ToString("N");
            await arrangeCtx.WebhookServiceClients.AddAsync(clientEntity);
            await arrangeCtx.SaveChangesAsync();

            clientId = clientEntity.ClientId;
        }

        _applicationHasherMock.Setup(x => x.HashSecret(It.IsAny<string>())).ReturnsAsync(hashedSecret);
        var request = BuildRequestNewClientKey(clientId: clientId);
        var sut = CreateSut(applicationHasher: _applicationHasherMock.Object);

        //Act
        var result = await sut.RequestNewClientKeyAsync(request);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Null(result.ResponseData);
        Assert.StartsWith("Service client does not exist:", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(clientId, result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    //-------------------
    //OnboardNewServiceClientAsync
    //-------------------
    [Fact]
    public async Task OnboardNewServiceClientAsync_CancellationRequested_Returns500InternalServerError()
    {
        //Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var request = BuildCreateServiceClient(catalogToSubscribe: ["customercreated"], clientId: "order-service-prod", serviceName: "Order Service");
        var sut = CreateSut();
        //Act
        var result = await sut.OnboardNewServiceClientAsync(request, cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.Null(result.ResponseData);
        Assert.Equal("An error occurred onboarding client.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task OnboardNewServiceClientAsync_ClientIdAlreadyExists_Returns409Conflict()
    {
        //Arrange
        var catalogIds = _createdEventCatalogs.Select(x => x.Id).ToList();
        var clientId = string.Empty;
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var clientEntity = BuildServiceClientEntity(subscribedCatalogs: catalogIds.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogIds.Count)).ToArray(), clientId: "customer-service-prod", clientName: "Customer Service");

            await arrangeCtx.WebhookServiceClients.AddAsync(clientEntity);
            await arrangeCtx.SaveChangesAsync();

            clientId = clientEntity.ClientId;
        }

        var request = BuildCreateServiceClient(catalogToSubscribe: ["customercreated"], clientId: clientId, serviceName: "Customer service");
        var sut = CreateSut();

        //Act
        var result = await sut.OnboardNewServiceClientAsync(request);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Conflict, result.HttpStatusCode);
        Assert.Null(result.ResponseData);
        Assert.StartsWith("Service Client already onboarded", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(request.ClientId, result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnboardNewServiceClientAsync_InvalidCatalogAdded_Returns404NotFound()
    {
        //Arrange
        var catalogNames = _createdEventCatalogs.Select(x => x.NormalizedEventName).ToList();
        var requestCatalogNames = catalogNames.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogNames.Count)).ToList();
        requestCatalogNames.Add("inventoryupdated");
        requestCatalogNames.Add("orderassigned");
        var request = BuildCreateServiceClient(catalogToSubscribe: requestCatalogNames.ToArray(), clientId: "order-service-prod", serviceName: "Order Service");

        var sut = CreateSut();

        //Act
        var result = await sut.OnboardNewServiceClientAsync(request);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Null(result.ResponseData);
        Assert.StartsWith("One or more provided event catalog(s) does not exist", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inventoryupdated", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orderassigned", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnboardNewServiceClientAsync_UserNotExists_Returns403Forbiddedn()
    {
        //Arrange
        var catalogNames = _createdEventCatalogs.Select(x => x.NormalizedEventName).ToList();
        var requestCatalogNames = catalogNames.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogNames.Count)).ToList();

        var request = BuildCreateServiceClient(catalogToSubscribe: requestCatalogNames.ToArray(), clientId: "order-service-prod", serviceName: "Order Service");

        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        _authenticatedUserDetailsMock.Setup(x => x.emailAddress).Returns("userexample@mail.com");
        var sut = CreateSut(authenticatedUserDetails: _authenticatedUserDetailsMock.Object);

        //Act
        var result = await sut.OnboardNewServiceClientAsync(request);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Forbidden, result.HttpStatusCode);
        Assert.Null(result.ResponseData);
        Assert.Equal("Unauthorized Access.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task OnboardNewServiceClientAsync_ValidRequest_Returns201Created()
    {
        //Arrange
        Guid userId = Guid.NewGuid();
        string userEmail = string.Empty;
        string clientId = Random.Shared.GetHexString(6);
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            User userToCreate = new User()
            {
                UserName = "TestUser112",
                NormalizedUserName = "TestUser112".ToUpper(),
                Email = "test@example.com",
                NormalizedEmail = "test@example.com".ToUpper(),
                FirstName = "John",
                LastName = "Doe",
                IsActive = true
            };


            await arrangeCtx.Users.AddAsync(userToCreate);
            await arrangeCtx.SaveChangesAsync();
            userId = userToCreate.Id; userEmail = userToCreate.NormalizedEmail;
            clientId = Random.Shared.GetHexString(6);
        }

        var catalogNames = _createdEventCatalogs.Select(x => x.NormalizedEventName).ToList();
        var requestCatalogNames = catalogNames.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, catalogNames.Count)).ToList();

        var request = BuildCreateServiceClient(catalogToSubscribe: requestCatalogNames.ToArray(), clientId: "order-service-prod", serviceName: "Order Service");

        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(userId.ToString("N"));
        _authenticatedUserDetailsMock.Setup(x => x.emailAddress).Returns(userEmail);
        var sut = CreateSut(authenticatedUserDetails: _authenticatedUserDetailsMock.Object);

        //Act
        var result = await sut.OnboardNewServiceClientAsync(request);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Created, result.HttpStatusCode);
        Assert.NotNull(result.ResponseData);

        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var createdClient = await assertCtx.WebhookServiceClients.Include(x => x.EventCatalogs).FirstOrDefaultAsync(x => x.ClientId == request.ClientId);

        Assert.NotNull(createdClient);
        Assert.True(createdClient.IsActive);
        Assert.Equal(request.AllowedEventTypes.Count, createdClient.EventCatalogs.Count);
    }
}
