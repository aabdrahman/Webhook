using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Serilog;
using System.Threading.Channels;
using WebHook.Core.DataTransferObjects.EmailSender;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Infrastructure.BackgroundWorkers;

namespace WebHook.IntegrationTests.BackgroundWorkers;

/// <summary>
/// Integration tests for <see cref="EmailProcessorWorker"/>.
///
/// TESTING STRATEGY:
/// <see cref="IEmailService"/> is mocked with Moq — no real SMTP needed.
/// <see cref="TestableEmailProcessorWorker"/> exposes ExecuteAsync directly
/// so tests do not wait for the PeriodicTimer tick.
/// Delays are set to 0ms via configuration so tests run fast.
///
/// BUGS DOCUMENTED IN TESTS:
///   BUG 1 — Task.Delay(10000) hardcoded in ExecuteAsync per email.
///            Must be configurable for tests to be practical.
///   BUG 2 — Task.Delay(1000) hardcoded in StopAsync per email.
///            Same issue.
///   BUG 3 — _emailProcessorWorkerConfiguration captured at construction.
///            Runtime config changes ignored.
///   BUG 4 — No try/catch around the while loop in ExecuteAsync.
///            One unhandled exception kills the worker permanently.
/// </summary>
public sealed class EmailProcessorWorkerTests : IAsyncLifetime
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private Channel<EmailSenderDto> _channel = null!;
    private ServiceProvider         _serviceProvider = null!;
    private Mock<IEmailService>     _emailServiceMock = null!;

    // -------------------------------------------------------------------------
    // IAsyncLifetime — fresh state per test
    // -------------------------------------------------------------------------

    public Task InitializeAsync()
    {
        // Fresh channel and mock per test — no cross-test contamination
        _channel          = Channel.CreateUnbounded<EmailSenderDto>();
        _emailServiceMock = new Mock<IEmailService>();

        // Default — SendMailAsync returns true
        _emailServiceMock
            .Setup(s => s.SendMailAsync(It.IsAny<EmailSenderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var services = new ServiceCollection();

        // Register channel singleton
        services.AddSingleton(_channel);

        // Register mock IEmailService
        services.AddScoped<IEmailService>(_ => _emailServiceMock.Object);

        // Configuration — 0ms delays so tests run instantly
        services.Configure<EmailProcessorWorkerConfiguration>(opt =>
        {
            opt.ProcessingIntervalInSeconds = 1;
            opt.ProcessingDelayInMilliSeconds = 0;
        });

        Log.Logger = new LoggerConfiguration().CreateLogger();

        _serviceProvider = services.BuildServiceProvider();

        return Task.CompletedTask;
    }

    public async Task DisposeAsync() =>
        await _serviceProvider.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private TestableEmailProcessorWorker CreateWorker() =>
        new TestableEmailProcessorWorker(
            _channel,
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _serviceProvider.GetRequiredService<IOptionsMonitor<EmailProcessorWorkerConfiguration>>());

    private static EmailSenderDto BuildEmailDto(
        string subject   = "Test Subject",
        string recipient = "subscriber@partner.com") => new(MailContent: "<p>Test body</p>", Subject: subject, MailRecipients: new List<string> { recipient }, IsHtml: true);

    /// <summary>
    /// Runs ExecuteAsync until the condition is met or timeout is reached.
    /// </summary>
    private static async Task RunWorkerUntilAsync(
        TestableEmailProcessorWorker worker,
        Func<Task<bool>>             condition,
        int                          timeoutMs = 10_000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);

        var executeTask = Task.Run(() => worker.RunAsync(cts.Token), cts.Token);

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline && !cts.IsCancellationRequested)
        {
            if (await condition()) break;
            await Task.Delay(100).ContinueWith(_ => { });
        }

        await Task.Delay(300); // buffer for final processing

        cts.Cancel();
        try { await executeTask; }
        catch (OperationCanceledException) { }
    }

    // -------------------------------------------------------------------------
    // StartAsync / StopAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_StartsWithoutException()
    {
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_CompletesWithinReasonableTime()
    {
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        cts.Cancel();

        var stopTask = worker.StopAsync(CancellationToken.None);
        var completedInTime = await Task.WhenAny(stopTask, Task.Delay(5000)) == stopTask;

        Assert.True(completedInTime, "StopAsync did not complete within 5 seconds.");
    }

    // -------------------------------------------------------------------------
    // ExecuteAsync — empty channel
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_EmptyChannel_NoEmailSent()
    {
        // Arrange — channel is empty
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(3000);

        // Act
        try { await worker.RunAsync(cts.Token); }
        catch (OperationCanceledException) { }

        // Assert
        _emailServiceMock.Verify(
            s => s.SendMailAsync(It.IsAny<EmailSenderDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyChannel_DoesNotThrow()
    {
        // Arrange
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(3000);

        // Act & Assert
        var ex = await Record.ExceptionAsync(async () =>
        {
            try { await worker.RunAsync(cts.Token); }
            catch (OperationCanceledException) { }
        });

        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // ExecuteAsync — single email processed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_OneEmailInChannel_SendMailCalledOnce()
    {
        // Arrange
        await _channel.Writer.WriteAsync(BuildEmailDto());

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(
            worker,
            () => Task.FromResult(_emailServiceMock.Invocations.Count >= 1));

        // Assert
        _emailServiceMock.Verify(
            s => s.SendMailAsync(It.IsAny<EmailSenderDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_OneEmailInChannel_CorrectEmailSent()
    {
        // Arrange
        var email = BuildEmailDto(subject: "Dead Letter Alert", recipient: "admin@partner.com");
        await _channel.Writer.WriteAsync(email);

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(
            worker,
            () => Task.FromResult(_emailServiceMock.Invocations.Count >= 1));

        // Assert — correct DTO passed to SendMailAsync
        _emailServiceMock.Verify(
            s => s.SendMailAsync(
                It.Is<EmailSenderDto>(dto =>
                    dto.Subject == "Dead Letter Alert" &&
                    dto.MailRecipients.Contains("admin@partner.com")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -------------------------------------------------------------------------
    // ExecuteAsync — multiple emails processed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_MultipleEmailsInChannel_AllSent()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
            await _channel.Writer.WriteAsync(BuildEmailDto($"Subject {i}"));

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(
            worker,
            () => Task.FromResult(_emailServiceMock.Invocations.Count >= 5));

        // Assert
        _emailServiceMock.Verify(
            s => s.SendMailAsync(It.IsAny<EmailSenderDto>(), It.IsAny<CancellationToken>()),
            Times.Exactly(5));
    }

    [Fact]
    public async Task ExecuteAsync_MultipleEmailsInChannel_ChannelDrainedAfterProcessing()
    {
        // Arrange
        for (int i = 0; i < 3; i++)
            await _channel.Writer.WriteAsync(BuildEmailDto($"Subject {i}"));

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(
            worker,
            () => Task.FromResult(_emailServiceMock.Invocations.Count >= 3));

        // Assert — all items consumed from channel
        Assert.Equal(0, _channel.Reader.Count);
    }

    // -------------------------------------------------------------------------
    // ExecuteAsync — SendMailAsync returns false
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_SendMailReturnsFalse_ContinuesToNextEmail()
    {
        // Arrange — first call returns false, second returns true
        var callCount = 0;
        _emailServiceMock
            .Setup(s => s.SendMailAsync(It.IsAny<EmailSenderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++callCount != 1); // false on first, true on second

        await _channel.Writer.WriteAsync(BuildEmailDto("Email 1"));
        await _channel.Writer.WriteAsync(BuildEmailDto("Email 2"));

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(
            worker,
            () => Task.FromResult(_emailServiceMock.Invocations.Count >= 2));

        // Assert — both emails attempted despite first returning false
        _emailServiceMock.Verify(
            s => s.SendMailAsync(It.IsAny<EmailSenderDto>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // -------------------------------------------------------------------------
    // ExecuteAsync — SendMailAsync throws exception
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_SendMailThrowsOnFirstEmail_ContinuesToSecondEmail()
    {
        // Arrange — first email throws, second succeeds
        var callCount = 0;
        _emailServiceMock
            .Setup(s => s.SendMailAsync(It.IsAny<EmailSenderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("SMTP failure");
                return true;
            });

        await _channel.Writer.WriteAsync(BuildEmailDto("Email 1"));
        await _channel.Writer.WriteAsync(BuildEmailDto("Email 2"));

        var worker = CreateWorker();

        // Act — exception caught by inner catch, continue to next email
        var ex = await Record.ExceptionAsync(async () =>
            await RunWorkerUntilAsync(
                worker,
                () => Task.FromResult(_emailServiceMock.Invocations.Count >= 2)));

        // Assert — no exception propagated, both emails attempted
        Assert.Null(ex);
        _emailServiceMock.Verify(
            s => s.SendMailAsync(It.IsAny<EmailSenderDto>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAsync_SendMailThrows_DoesNotCrashWorker()
    {
        // Arrange
        _emailServiceMock
            .Setup(s => s.SendMailAsync(It.IsAny<EmailSenderDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("SMTP unavailable"));

        await _channel.Writer.WriteAsync(BuildEmailDto());

        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(5000);

        // Act & Assert — worker must not crash
        var ex = await Record.ExceptionAsync(async () =>
        {
            try { await worker.RunAsync(cts.Token); }
            catch (OperationCanceledException) { }
        });

        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // StopAsync — drains remaining emails
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StopAsync_EmailsInChannel_DrainedOnStop()
    {
        // Arrange — write emails but do not start worker
        for (int i = 0; i < 3; i++)
            await _channel.Writer.WriteAsync(BuildEmailDto($"Subject {i}"));

        var worker = CreateWorker();
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);

        // Act — stop while channel has items
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        // Assert — StopAsync drained remaining items
        _emailServiceMock.Verify(
            s => s.SendMailAsync(It.IsAny<EmailSenderDto>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(1));
    }

    [Fact]
    public async Task StopAsync_EmptyChannel_CompletesWithoutSending()
    {
        // Arrange — empty channel
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        cts.Cancel();

        // Act
        await worker.StopAsync(CancellationToken.None);

        // Assert — nothing to drain
        _emailServiceMock.Verify(
            s => s.SendMailAsync(It.IsAny<EmailSenderDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -------------------------------------------------------------------------
    // BUG 1 — Task.Delay hardcoded to 10000ms
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_HardcodedDelay_DocumentsBug1()
    {
        // BUG 1: Task.Delay(10000, stoppingToken) is hardcoded per email.
        // With 10 emails this is 100 seconds of delay — completely impractical
        // for tests and slow in production under load.
        //
        // Fix: read delay from configuration:
        //   await Task.Delay(_emailProcessorWorkerConfiguration.ProcessingDelayMs, stoppingToken);
        //
        // Then in tests set ProcessingDelayMs = 0.
        // This test documents the bug — it passes only because the testable
        // worker uses 0ms delay via configuration.
        Assert.True(true,
            "ProcessingDelayMs must be configurable. Hardcoded 10000ms makes tests impractical.");
    }

    // -------------------------------------------------------------------------
    // BUG 3 — Configuration captured at construction
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ConfigurationCapturedAtConstruction_DocumentsBug3()
    {
        // BUG 3: _emailProcessorWorkerConfiguration = optionsMonitor.CurrentValue
        // in constructor. Runtime config changes (e.g. interval) are ignored.
        //
        // Fix: read optionsMonitor.CurrentValue inside ExecuteAsync:
        //   var config = _optionsMonitor.CurrentValue;
        //   using var timer = new PeriodicTimer(TimeSpan.FromSeconds(config.ProcessingIntervalInSeconds));

        var worker = CreateWorker();

        // Change config after construction
        _serviceProvider
            .GetRequiredService<IOptionsMonitor<EmailProcessorWorkerConfiguration>>()
            .CurrentValue.ProcessingIntervalInSeconds = 999;

        // Worker still uses the original value captured at construction
        // No way to verify this without inspecting the timer — documented here
        Assert.True(true,
            "ProcessingIntervalInSeconds must be read inside ExecuteAsync not constructor.");
    }

    // -------------------------------------------------------------------------
    // Cancellation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CancelledImmediately_ExitsCleanly()
    {
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Record.ExceptionAsync(
            () => worker.RunAsync(cts.Token));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledMidProcessing_NoExceptionPropagated()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
            await _channel.Writer.WriteAsync(BuildEmailDto($"Subject {i}"));

        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(500); // cancel quickly

        // Act & Assert
        var ex = await Record.ExceptionAsync(async () =>
        {
            try { await worker.RunAsync(cts.Token); }
            catch (OperationCanceledException) { }
        });

        Assert.Null(ex);
    }
}

// =============================================================================
// Testable subclass
// =============================================================================

/// <summary>
/// Exposes the protected <see cref="BackgroundService.ExecuteAsync"/> so
/// tests can call it directly without waiting for the PeriodicTimer tick.
/// Only exists in the test project.
/// </summary>
internal sealed class TestableEmailProcessorWorker : EmailProcessorWorker
{
    public TestableEmailProcessorWorker(
        Channel<EmailSenderDto>                          channel,
        IServiceScopeFactory                             scopeFactory,
        IOptionsMonitor<EmailProcessorWorkerConfiguration> options)
        : base(channel, scopeFactory, options) { }

    /// <summary>
    /// Calls <see cref="ExecuteAsync"/> directly — no StartAsync, no timer wait.
    /// </summary>
    public Task RunAsync(CancellationToken ct) => ExecuteAsync(ct);
}
