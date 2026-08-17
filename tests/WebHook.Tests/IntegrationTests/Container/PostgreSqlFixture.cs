using Testcontainers.PostgreSql;

namespace WebHook.IntegrationTests.BackgroundWorkers;

/// <summary>
/// Shared Testcontainers fixture that starts a single PostgreSQL container
/// for the entire test class. Using <see cref="IClassFixture{T}"/> means the
/// container starts once, all tests in the class share it, and it is
/// disposed after the last test completes.
///
/// The container uses postgres:16-alpine — lightweight and fast to pull.
/// Each test resets the schema via EnsureDeleted / EnsureCreated in
/// IAsyncLifetime.InitializeAsync rather than restarting the container,
/// keeping the suite fast.
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    //[Obsolete]
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(image: "postgres:16-alpine")
        //.WithImage("postgres:16-alpine")
        .WithDatabase("webhook_test")
        .WithUsername("test_user")
        .WithPassword("test_password")
        .WithCleanUp(true) // container removed automatically after tests
        .Build();

    /// <summary>
    /// The connection string for the running PostgreSQL container.
    /// Available after <see cref="InitializeAsync"/> completes.
    /// </summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() =>
        await _container.StartAsync();

    public async Task DisposeAsync() =>
        await _container.DisposeAsync();
}
