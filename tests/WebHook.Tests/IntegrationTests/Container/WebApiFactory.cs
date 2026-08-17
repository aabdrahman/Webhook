using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using WebHook.Core.Interfaces.Services;

namespace WebHook.IntegrationTests.Controllers;

/// <summary>
/// A shared <see cref="WebApplicationFactory{TEntryPoint}"/> that replaces
/// all service dependencies with Moq mocks so controller tests run in-process
/// without any database, SMTP, or external service.
///
/// Each test class receives its own factory instance via
/// <see cref="IAsyncLifetime"/> so mocks are fresh per test class.
/// </summary>
public sealed class WebApiFactory : WebApplicationFactory<Program>
{
    // Public mock properties so test classes can set up and verify behaviour
    public Mock<IAuthenticationService> AuthenticationServiceMock { get; } = new();
    public Mock<IUserService> UserServiceMock { get; } = new();
    public Mock<IOtpService> OtpServiceMock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace real service registrations with mocks
            services.RemoveAll<IAuthenticationService>();
            services.RemoveAll<IUserService>();
            services.RemoveAll<IOtpService>();

            services.AddSingleton(AuthenticationServiceMock.Object);
            services.AddSingleton(UserServiceMock.Object);
            services.AddSingleton(OtpServiceMock.Object);
        });
    }
}
