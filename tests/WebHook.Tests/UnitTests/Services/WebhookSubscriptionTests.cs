using Microsoft.AspNetCore.Identity;
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
    private readonly Mock<IAuthenticatedUserDetails> _authenticatedUserDetailsMock;

    public WebhookSubscriptionTests()
    {

        _dbContextOptions = new DbContextOptionsBuilder<RepositoryContext>()
                                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                                    .Options;

        Log.Logger = new LoggerConfiguration().CreateLogger();

        //Secret Configuration Mocking
        _signatureSecretConfigurationMocker = new Mock<IOptionsMonitor<SignatureSecretConfiguration>>();
        _signatureSecretConfigurationMocker.Setup(ssc => ssc.CurrentValue).Returns(new SignatureSecretConfiguration() { KeySize = 32 });

        //Secret Key Generator Mocker
        _secretGeneratorMock = new Mock<ISecretKeyGenerator>();
        _secretGeneratorMock.Setup(sg => sg.GenerateKey(It.IsAny<int>())).Returns("my-secret");

        //Encryption Service Mocker
        _encryptionServiceMock = new Mock<IEncryptionService>();
        _encryptionServiceMock.Setup(es => es.Encrypt(It.IsAny<string>())).Returns("my-encrypted-secret");

        //Authenticated User Mock
        _authenticatedUserDetailsMock = new Mock<IAuthenticatedUserDetails>();

        //Databse creation at test initializer.
        using var ctx = new RepositoryContext(_dbContextOptions);
        ctx.Database.EnsureCreated();
        ctx.WebHookEventCatalogs.AddRange(GetEventCatalogs());
        ctx.SaveChanges();
    }

    private (RepositoryContext ctx, WebhookSubscriptionService svc) GetSut(IAuthenticatedUserDetails authenticatedUserDetails = null)
    {
        var context = new RepositoryContext(_dbContextOptions);
        return (context, new WebhookSubscriptionService(context, _secretGeneratorMock.Object, _signatureSecretConfigurationMocker.Object, _encryptionServiceMock.Object, authenticatedUserDetails ?? _authenticatedUserDetailsMock.Object));
    }

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
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
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
        using var requestCtx = new RepositoryContext(_dbContextOptions);
        (Guid Id, string Email, string UserName) seededUser = await TestDataSeeder.SeedUserAsync(requestCtx);

        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(seededUser.Id.ToString("N"));
        var sut = GetSut(_authenticatedUserDetailsMock.Object);
        var createEntity = BuildCreateDto();

        //Act
        var result = await sut.svc.CreateWebhookSubscriptionAsync(createEntity);
        var fetchResult = await sut.svc.GetAllWebhookSubscriptionAsync();
        var subscriptionFromDb = await sut.ctx.WebhookSubscriptions.ToListAsync();

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Created, result.HttpStatusCode);
        Assert.Equal("You have successfully subscribed to the webhook.", result.ResponseMessage, ignoreCase: true);

        Assert.NotNull(fetchResult);
        Assert.NotNull(fetchResult.ResponseData);
        Assert.Contains(fetchResult.ResponseData, x => x.Name == createEntity.SubscriberName);
        Assert.Single(subscriptionFromDb);
        Assert.Equal(seededUser.Id, subscriptionFromDb.First().CreatedByUserId);
    }

    [Fact]
    public async Task CreateWebhookSubscriptionAsync_InValidRequest_Returns400BadRequest()
    {
        //Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var sut = GetSut();
        var createEntity = BuildCreateDto();
        createEntity.SubscribedEvents.Add("EventAdded");

        //Act
        var result = await sut.svc.CreateWebhookSubscriptionAsync(createEntity);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("One or more events to subscribe does not exist.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task CreateWebhookSubscriptionAsync_UserIdNotExists_Returns404NotFound()
    {
        //Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var sut = GetSut(_authenticatedUserDetailsMock.Object);
        var createEntity = BuildCreateDto();
        //createEntity.SubscribedEvents.Add("EventAdded");

        //Act
        var result = await sut.svc.CreateWebhookSubscriptionAsync(createEntity);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.StartsWith("User with id does not exists -", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateWebhookSubscriptionAsync_UnparsableUserId_Returns400BadRequset()
    {
        //Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(string.Concat(Guid.NewGuid().ToString("N"), "234"));
        var sut = GetSut(_authenticatedUserDetailsMock.Object);
        var createEntity = BuildCreateDto();
        createEntity.SubscribedEvents.Add("EventAdded");

        //Act
        var result = await sut.svc.CreateWebhookSubscriptionAsync(createEntity);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("User details could not be fetched successfully.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task GetUserSubscriptionsAsync_CancellationRequested_Returns500InternalServerError()
    {
        //Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var sut = GetSut(_authenticatedUserDetailsMock.Object);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        //Act
        var result = await sut.svc.GetUserSubscriptionsAsync(ct: cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.ErrorDetail);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.Equal("An error occurred while fetching user subscriptions.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task GetUserSubscriptionsAsync_UnparsableUserId_Returns403Forbidden()
    {
        //Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(string.Concat(Guid.NewGuid().ToString("N"), "1234"));
        var sut = GetSut(_authenticatedUserDetailsMock.Object);

        //Act
        var result = await sut.svc.GetUserSubscriptionsAsync(CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ErrorDetail);
        Assert.Equal(HttpStatusCode.Forbidden, result.HttpStatusCode);
        Assert.Equal("Invalid User details.", result.ResponseMessage, ignoreCase: true);

    }

    [Fact]
    public async Task GetUserSubscriptionsAsync_NoSubscriptionForUserId_Retruns404NotFound()
    {
        //Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var sut = GetSut(_authenticatedUserDetailsMock.Object);

        //Act
        var result = await sut.svc.GetUserSubscriptionsAsync();

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ErrorDetail);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("No subscriptions for authenticated user.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task GetUserSubscriptionsAsync_SubscriptionExistsForUserId_Returns200OK()
    {
        //Arrange
        var requestCts = GetSut();
        var seededUser = await TestDataSeeder.SeedUserAsync(requestCts.ctx);

        var existingEventCatalogs = await requestCts.ctx.WebHookEventCatalogs.ToListAsync();
        List<WebhookSubscription> webhookSubscriptions =
            [
                BuildEntity("user 1", existingEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, 4)).Select(x => x.Id).ToList()),
                BuildEntity("user 2", existingEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, 4)).Select(x => x.Id).ToList()),
                BuildEntity("user 3", existingEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, 4)).Select(x => x.Id).ToList())
            ];
        webhookSubscriptions.ForEach(x => x.CreatedByUserId = seededUser.Id);

        await requestCts.ctx.WebhookSubscriptions.AddRangeAsync(webhookSubscriptions);
        await requestCts.ctx.SaveChangesAsync();

        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(seededUser.Id.ToString("N"));
        var sut = GetSut(_authenticatedUserDetailsMock.Object);

        //Act
        var result = await sut.svc.GetUserSubscriptionsAsync(CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.Null(result.ErrorDetail);
        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("Subscriptions fetched successfully.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(webhookSubscriptions.Count, result.ResponseData.Count);
    }

    [Fact]
    public async Task GetUserSubscriptionsAsync_SubscriptionSpooledForActualUserId_Returns200OK()
    {
        //Arrange
        var requestCts = GetSut();
        var seededUser = await TestDataSeeder.SeedUserAsync(requestCts.ctx);
        var seededUser2 = await TestDataSeeder.SeedUserAsync(requestCts.ctx, "testuser2@test.com", "testuser2");

        var existingEventCatalogs = await requestCts.ctx.WebHookEventCatalogs.ToListAsync();
        List<WebhookSubscription> webhookSubscriptions =
            [
                BuildEntity("user 1", existingEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, 4)).Select(x => x.Id).ToList()),
                BuildEntity("user 2", existingEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, 4)).Select(x => x.Id).ToList()),
                BuildEntity("user 3", existingEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, 4)).Select(x => x.Id).ToList())
            ];
        webhookSubscriptions.Where(x => x.Name.EndsWith("2")).ToList().ForEach(x => x.CreatedByUserId = seededUser.Id);
        webhookSubscriptions.Where(x => !x.Name.EndsWith("2")).ToList().ForEach(x => x.CreatedByUserId = seededUser2.Id);

        await requestCts.ctx.WebhookSubscriptions.AddRangeAsync(webhookSubscriptions);
        await requestCts.ctx.SaveChangesAsync();

        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(seededUser.Id.ToString("N"));
        var sut = GetSut(_authenticatedUserDetailsMock.Object);

        //Act
        var result = await sut.svc.GetUserSubscriptionsAsync(CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.Null(result.ErrorDetail);
        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("Subscriptions fetched successfully.", result.ResponseMessage, ignoreCase: true);
        Assert.Single(result.ResponseData);
    }
}

/// <summary>
/// Provides static seeding utilities for inserting test data directly
/// into a <see cref="RepositoryContext"/> backed by an in-memory database.
///
/// These methods bypass the Identity pipeline intentionally — UserManager
/// and RoleManager are not needed for unit tests that only care about
/// entity relationships, not authentication mechanics.
/// </summary>
public static class TestDataSeeder
{
    /// <summary>
    /// Seeds a <see cref="User"/> and the USER <see cref="Role"/> into the
    /// provided context, links them via <c>AspNetUserRoles</c>, and returns
    /// the created user's Id, normalised email, and normalised username.
    /// </summary>
    /// <remarks>
    /// Inserts the role only if it does not already exist so this method
    /// is safe to call multiple times within the same test class without
    /// causing duplicate key violations.
    /// </remarks>
    /// <param name="context">
    /// The <see cref="RepositoryContext"/> to seed. Must be using an
    /// in-memory database — calling this against a real database is
    /// not recommended as it bypasses Identity password hashing.
    /// </param>
    /// <param name="email">Email address for the new user. Must be unique within the test database.</param>
    /// <param name="userName">Username for the new user. Must be unique within the test database.</param>
    /// <returns>
    /// A tuple of (<c>Id</c>, <c>Email</c>, <c>UserName</c>) for the created user,
    /// all normalised to uppercase to match Identity conventions.
    /// </returns>
    public static async Task<(Guid Id, string Email, string UserName)> SeedUserAsync(
        RepositoryContext context,
        string email = "testuser@test.com",
        string userName = "testuser")
    {
        // Ensure the USER role exists — insert only if absent
        var role = await context.Roles.FirstOrDefaultAsync(r => r.Name == "USER");
        if (role is null)
        {
            role = new Role
            {
                Id = Guid.NewGuid(),
                Name = "USER",
                NormalizedName = "USER",
                Description = "Standard user role.",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            await context.Roles.AddAsync(role);
            await context.SaveChangesAsync();
        }

        // Build and insert the user
        var user = new User
        {
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "User",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            IsActive = true,
            EmailConfirmed = true,
            CreatedAt = DateTimeOffset.UtcNow,
            PasswordHash = "not-used-in-tests",
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };

        await context.Users.AddAsync(user);
        await context.SaveChangesAsync();

        // Link user to role via the Identity join table
        await context.UserRoles.AddAsync(new IdentityUserRole<Guid>
        {
            UserId = user.Id,
            RoleId = role.Id
        });
        await context.SaveChangesAsync();

        return (user.Id, user.NormalizedEmail!, user.NormalizedUserName!);
    }
}