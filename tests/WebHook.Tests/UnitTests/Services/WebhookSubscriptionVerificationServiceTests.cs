using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using WebHook.Core.Constants;
using WebHook.Core.Entities;
using WebHook.Core.EventContracts.Publishers;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Services;
using WebHook.IntegrationTests.BackgroundWorkers;

namespace WebHook.Tests.UnitTests.Services;

public class WebhookSubscriptionVerificationServiceTests : IAsyncDisposable
{
    private readonly DbContextOptions<RepositoryContext> _dbContextOptions;
    private Mock<IAuthenticatedUserDetails> _authenticatedUserMock;
    private Mock<ISecretKeyGenerator> _secretKeyGeneratorMock;
    private Mock<IApplicationPublisher> _applicationPublisherMock;
    public WebhookSubscriptionVerificationServiceTests()
    {
        _dbContextOptions = new DbContextOptionsBuilder<RepositoryContext>()
            .UseInMemoryDatabase(databaseName: $"{Guid.NewGuid().ToString()}")
            .Options;

        _authenticatedUserMock = new Mock<IAuthenticatedUserDetails>();
        _secretKeyGeneratorMock = new Mock<ISecretKeyGenerator>();
        _applicationPublisherMock = new Mock<IApplicationPublisher>();

        using var ctx = new RepositoryContext(_dbContextOptions);
        ctx.Database.EnsureCreated();
        //ctx.Database.Migrate();

        ctx.WebHookEventCatalogs.AddRange(GetEventCatalogs());
        ctx.SaveChanges();
    }
    public async ValueTask DisposeAsync()
    {
        using var ctx = new RepositoryContext(_dbContextOptions);
        await ctx.Database.EnsureDeletedAsync();
    }

    private SubscriptionVerificationService CreateSut(IAuthenticatedUserDetails authenticatedUserDetails = null, ISecretKeyGenerator secretKeyGenerator = null, IApplicationPublisher applicationPublisher = null)
    {
        var ctx = new RepositoryContext(_dbContextOptions);
        return new SubscriptionVerificationService(authenticatedUserDetails: authenticatedUserDetails ?? _authenticatedUserMock.Object, secretKeyGenerator: secretKeyGenerator ?? _secretKeyGeneratorMock.Object, 
            applicationPublisher: applicationPublisher ?? _applicationPublisherMock.Object, repositoryContext: ctx);
    }

    //Uttiliies
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

    //VerifySubscriptionCallbackUrl Tests
    [Fact]
    public async Task VerifySubscriptionCallbackUrl_CancellationRequested_ShouldThrowOperationCanceledException()
    {
        // Arrange
        _authenticatedUserMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString());
        var sut = CreateSut(authenticatedUserDetails: _authenticatedUserMock.Object);
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        //Act
        var result = await sut.VerifySubscriptionCallbackUrl(Guid.NewGuid(), cancellationTokenSource.Token);

        // Assert
        //await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        //    await sut.VerifySubscriptionCallbackUrl(Guid.NewGuid(), cancellationTokenSource.Token));
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("An error occurred processing request.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal("Operation Failed.", result.ResponseData);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
    }

    [Fact]
    public async Task VerifySubscriptionCallbackUrl_InvalidUserId_ShouldReturnFailureResponse()
    {
        // Arrange
        _authenticatedUserMock.Setup(x => x.userId).Returns("invalid-guid");
        var sut = CreateSut(authenticatedUserDetails: _authenticatedUserMock.Object);
        // Act
        var result = await sut.VerifySubscriptionCallbackUrl(Guid.NewGuid());
        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal("An error occurred processing request.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal("Operation Failed.", result.ResponseData);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
    }

    [Fact]
    public async Task VerifySubscriptionCallbackUrl_SubscriptionNotFound_ShouldReturnFailureResponse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _authenticatedUserMock.Setup(x => x.userId).Returns(userId.ToString());
        var sut = CreateSut(authenticatedUserDetails: _authenticatedUserMock.Object);
        // Act
        var result = await sut.VerifySubscriptionCallbackUrl(Guid.NewGuid());
        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal("Subscription not found.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal("Operation Failed.", result.ResponseData);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
    }

    [Fact]
    public async Task VerifySubscriptionCallbackUrl_SubscriptionBelongToAnotherUser_ShouldReturnFailureResponse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _authenticatedUserMock.Setup(x => x.userId).Returns(userId.ToString());
        var sut = CreateSut(authenticatedUserDetails: _authenticatedUserMock.Object);
        var subscription = BuildEntity("Test Subscription", new List<Guid>());
        subscription.CreatedByUserId = userId;
        subscription.OnboardStatus = SubscriptionOnboardStatus.Active;
        using (var ctx = new RepositoryContext(_dbContextOptions))
        {
            (Guid Id, string Email, string UserName) seededUser = await TestDataSeeder.SeedUserAsync(ctx);
            subscription.CreatedByUserId = seededUser.Id;
            await ctx.WebhookSubscriptions.AddAsync(subscription);
            await ctx.SaveChangesAsync();
        }
        // Act
        var result = await sut.VerifySubscriptionCallbackUrl(Guid.NewGuid());
        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal("Subscription not found.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal("Operation Failed.", result.ResponseData);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
    }

    [Fact]
    public async Task VerifySubscriptionCallbackUrl_SubscriptionNotInactive_ShouldReturnFailureResponse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _authenticatedUserMock.Setup(x => x.userId).Returns(userId.ToString());
        var sut = CreateSut(authenticatedUserDetails: _authenticatedUserMock.Object);
        var subscription = BuildEntity("Test Subscription", new List<Guid>());
        subscription.CreatedByUserId = userId;
        subscription.OnboardStatus = SubscriptionOnboardStatus.Active;
        using (var ctx = new RepositoryContext(_dbContextOptions))
        {
            (Guid Id, string Email, string UserName) seededUser = await TestDataSeeder.SeedUserAsync(ctx);
            subscription.CreatedByUserId = seededUser.Id;
            userId = seededUser.Id;
            await ctx.WebhookSubscriptions.AddAsync(subscription);
            await ctx.SaveChangesAsync();
        }
        // Act
        _authenticatedUserMock.Setup(x => x.userId).Returns(userId.ToString());
        sut = CreateSut(authenticatedUserDetails: _authenticatedUserMock.Object);
        var result = await sut.VerifySubscriptionCallbackUrl(subscription.Id);
        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal("Subscription is not in inactive status.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal("Operation Failed.", result.ResponseData);
        Assert.Equal(HttpStatusCode.Conflict, result.HttpStatusCode);
    }

    [Fact]
    public async Task VerifySubscriptionCallbackUrl_ValidRequest_ShouldReturnSuccessResponse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _authenticatedUserMock.Setup(x => x.userId).Returns(userId.ToString());
        _secretKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<int>())).Returns("test-challenge-token");
        var sut = CreateSut(authenticatedUserDetails: _authenticatedUserMock.Object, secretKeyGenerator: _secretKeyGeneratorMock.Object);
        var subscription = BuildEntity("Test Subscription", new List<Guid>());
        subscription.CreatedByUserId = userId;
        subscription.OnboardStatus = SubscriptionOnboardStatus.Inactive;
        using (var ctx = new RepositoryContext(_dbContextOptions))
        {
            (Guid Id, string Email, string UserName) seededUser = await TestDataSeeder.SeedUserAsync(ctx);
            subscription.CreatedByUserId = seededUser.Id;
            userId = seededUser.Id;
            await ctx.WebhookSubscriptions.AddAsync(subscription);
            await ctx.SaveChangesAsync();
        }
        _authenticatedUserMock.Setup(x => x.userId).Returns(userId.ToString());
        sut = CreateSut(authenticatedUserDetails: _authenticatedUserMock.Object, secretKeyGenerator: _secretKeyGeneratorMock.Object);
        // Act
        var result = await sut.VerifySubscriptionCallbackUrl(subscription.Id);
        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal("Operation Successful.", result.ResponseData);
    }
    [Fact]
    public async Task VerifySubscriptionCallbackUrl_ValidRequest_ShouldInsertVerificationRecord()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _authenticatedUserMock.Setup(x => x.userId).Returns(userId.ToString());
        _secretKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<int>())).Returns("test-challenge-token");
        var sut = CreateSut(authenticatedUserDetails: _authenticatedUserMock.Object, secretKeyGenerator: _secretKeyGeneratorMock.Object);
        var subscription = BuildEntity("Test Subscription", new List<Guid>());
        subscription.CreatedByUserId = userId;
        subscription.OnboardStatus = SubscriptionOnboardStatus.Inactive;
        using (var ctx = new RepositoryContext(_dbContextOptions))
        {
            (Guid Id, string Email, string UserName) seededUser = await TestDataSeeder.SeedUserAsync(ctx);
            subscription.CreatedByUserId = seededUser.Id;
            userId = seededUser.Id;
            await ctx.WebhookSubscriptions.AddAsync(subscription);
            await ctx.SaveChangesAsync();
        }
        _authenticatedUserMock.Setup(x => x.userId).Returns(userId.ToString());
        sut = CreateSut(authenticatedUserDetails: _authenticatedUserMock.Object, secretKeyGenerator: _secretKeyGeneratorMock.Object);
        // Act
        var result = await sut.VerifySubscriptionCallbackUrl(subscription.Id);
        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal("Operation Successful.", result.ResponseData);
        using (var ctx = new RepositoryContext(_dbContextOptions))
        {
            var verificationRecord = await ctx.WebhookSubscriptionVerifications.FirstOrDefaultAsync(v => v.SubscriptionId == subscription.Id);
            Assert.NotNull(verificationRecord);
            Assert.Equal(WebhookSubscriptionVerificationStatus.Pending, verificationRecord.Status);
            Assert.Equal("test-challenge-token", verificationRecord.ChallengeToken);
        }
    }

    //GetSubscriptionVerifications Tests
    [Fact]
    public async Task GetSubscriptionVerifications_CancellationRequested_ShouldThrowOperationCanceledException()
    {
        // Arrange
        _authenticatedUserMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString());
        var sut = CreateSut(authenticatedUserDetails: _authenticatedUserMock.Object);
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        //Act
        var result = await sut.GetSubscriptionVerifications(Guid.NewGuid(), cancellationTokenSource.Token);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal("An error occurred processing request.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
    }

    [Fact]
    public async Task GetSubscriptionVerifications_SubscriptionNotFound_ShouldReturnSuccessResponse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _authenticatedUserMock.Setup(x => x.userId).Returns(userId.ToString());
        var sut = CreateSut(authenticatedUserDetails: _authenticatedUserMock.Object);
        // Act
        var result = await sut.GetSubscriptionVerifications(Guid.NewGuid());
        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ResponseData);
        Assert.True(result.IsSuccessful);
        Assert.Equal("Subscription verification records fetched successfully.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
    }

    [Fact]
    public async Task GetSubscriptionVerifications_ValidRequest_ShouldReturnSuccessResponse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _authenticatedUserMock.Setup(x => x.userId).Returns(userId.ToString());
        var sut = CreateSut(authenticatedUserDetails: _authenticatedUserMock.Object);
        var subscription = BuildEntity("Test Subscription", new List<Guid>());
        subscription.CreatedByUserId = userId;
        subscription.OnboardStatus = SubscriptionOnboardStatus.Inactive;
        using (var ctx = new RepositoryContext(_dbContextOptions))
        {
            (Guid Id, string Email, string UserName) seededUser = await TestDataSeeder.SeedUserAsync(ctx);
            subscription.CreatedByUserId = seededUser.Id;
            userId = seededUser.Id;
            subscription.WebhookSubscriptionVerifications.Add(new WebhookSubscriptionVerification
            {
                Id = Guid.NewGuid(),
                SubscriptionId = subscription.Id,
                Status = WebhookSubscriptionVerificationStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                ChallengeToken = "test-challenge-token"
            });
            await ctx.WebhookSubscriptions.AddAsync(subscription);
            await ctx.SaveChangesAsync();
        }
        _authenticatedUserMock.Setup(x => x.userId).Returns(userId.ToString());
        sut = CreateSut(authenticatedUserDetails: _authenticatedUserMock.Object);
        // Act
        var result = await sut.GetSubscriptionVerifications(subscription.Id);
        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ResponseData);
        Assert.True(result.IsSuccessful);
        Assert.Equal("Subscription verification records fetched successfully.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Single(result.ResponseData);
    }
}
