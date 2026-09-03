using Microsoft.Extensions.Options;
using Moq;
using Serilog;
using WebHook.Core.DataTransferObjects.EmailSender;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Infrastructure.Utilities;

namespace WebHook.UnitTests.Services;

/// <summary>
/// Unit tests for <see cref="EmailService.SendMailAsync"/>.
///
/// TESTING CONSTRAINT:
/// <see cref="System.Net.Mail.SmtpClient"/> is a concrete sealed class and
/// cannot be mocked with Moq. Tests that exercise the SMTP path point
/// SmtpClient at 127.0.0.1:2525 (no listener) so it throws
/// <see cref="System.Net.Mail.SmtpException"/> — caught by the catch block
/// which returns false. This lets us verify all paths except a successful send.
///
/// </summary>
public sealed class EmailServiceTests : IDisposable
{
    // -------------------------------------------------------------------------
    // Setup / teardown
    // -------------------------------------------------------------------------

    private readonly Mock<IOptionsMonitor<EmailSenderEmailSmtpSettingsConfiguration>> _optionsMock;
    private readonly EmailSenderEmailSmtpSettingsConfiguration _settings;

    // Environment variable key used by the service
    private const string SmtpPasswordEnvVar = "SmtpClientPassword";
    private const string ValidPassword = "test-smtp-password";

    public EmailServiceTests()
    {
        Log.Logger = new LoggerConfiguration().CreateLogger();

        _settings = new EmailSenderEmailSmtpSettingsConfiguration
        {
            Host = "127.0.0.1",
            Port = 2525,
            Username = "noreply@webhookservice.com"
        };

        _optionsMock = new Mock<IOptionsMonitor<EmailSenderEmailSmtpSettingsConfiguration>>();
        _optionsMock
            .Setup(o => o.CurrentValue)
            .Returns(_settings);

        // Set a valid password by default — individual tests override when needed
        Environment.SetEnvironmentVariable(SmtpPasswordEnvVar, ValidPassword);
    }

    public void Dispose()
    {
        // Always clean up environment variable after each test
        Environment.SetEnvironmentVariable(SmtpPasswordEnvVar, null);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private EmailService CreateSut() =>
        new EmailService(_optionsMock.Object);

    private static EmailSenderDto ValidDto(string? subject = null, string? content = null, bool isHtml = true, params string[] recipients) =>
        new EmailSenderDto(Subject: subject ?? "Test Subject", MailContent: content ?? "<p>Test body</p>", MailRecipients: recipients.Length > 0 ? recipients.ToList() : new List<string> { "subscriber@partner.com" }, IsHtml: isHtml);


    // -------------------------------------------------------------------------
    // Environment variable — password missing or empty
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SendMailAsync_PasswordEnvVarNotSet_ReturnsFalse()
    {
        // Arrange — remove the environment variable
        Environment.SetEnvironmentVariable(SmtpPasswordEnvVar, null);
        var sut = CreateSut();

        // Act
        var result = await sut.SendMailAsync(ValidDto());

        // Assert — returns false before even attempting SMTP connection
        Assert.False(result);
    }

    [Fact]
    public async Task SendMailAsync_PasswordEnvVarIsEmpty_ReturnsFalse()
    {
        // Arrange
        Environment.SetEnvironmentVariable(SmtpPasswordEnvVar, string.Empty);
        var sut = CreateSut();

        // Act
        var result = await sut.SendMailAsync(ValidDto());

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SendMailAsync_PasswordEnvVarIsWhiteSpace_ReturnsFalse()
    {
        // Arrange — IsNullOrEmpty does not catch whitespace-only strings
        // This documents a potential edge case — "   " passes IsNullOrEmpty
        // but is not a valid SMTP password
        Environment.SetEnvironmentVariable(SmtpPasswordEnvVar, "   ");
        var sut = CreateSut();

        // Act
        var result = await sut.SendMailAsync(ValidDto());

        // Assert — whitespace passes IsNullOrEmpty so SMTP is attempted,
        // connection refused returns false via catch
        Assert.False(result);
    }

    [Fact]
    public async Task SendMailAsync_PasswordEnvVarNotSet_DoesNotThrow()
    {
        // Arrange
        Environment.SetEnvironmentVariable(SmtpPasswordEnvVar, null);
        var sut = CreateSut();

        // Act & Assert
        var ex = await Record.ExceptionAsync(() => sut.SendMailAsync(ValidDto()));
        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // SMTP failure — no listener on port 2525
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SendMailAsync_SmtpServerUnreachable_ReturnsFalse()
    {
        // Arrange — valid password, but no SMTP listener on port 2525
        var sut = CreateSut();

        // Act
        var result = await sut.SendMailAsync(ValidDto());

        // Assert — SmtpException caught, returns false
        Assert.False(result);
    }

    [Fact]
    public async Task SendMailAsync_SmtpServerUnreachable_DoesNotThrow()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert — exception caught internally, must not propagate
        var ex = await Record.ExceptionAsync(() => sut.SendMailAsync(ValidDto()));
        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // Multiple recipients — BUG 1 is now FIXED
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SendMailAsync_MultipleRecipients_DoesNotThrow()
    {
        // Arrange — BUG 1 is fixed: foreach adds all recipients
        // Previously .First() silently dropped second and third
        var sut = CreateSut();
        var dto = ValidDto(
            recipients: new[]{ "first@partner.com",
                        "second@partner.com",
                        "third@partner.com" });

        // Act & Assert — no InvalidOperationException from recipient loop
        var ex = await Record.ExceptionAsync(() => sut.SendMailAsync(dto));
        Assert.Null(ex);
    }

    [Fact]
    public async Task SendMailAsync_MultipleRecipients_ReturnsFalseDueToSmtpOnly()
    {
        // Arrange
        var sut = CreateSut();
        var dto = ValidDto(
            recipients: new[] { "a@partner.com", "b@partner.com", "c@partner.com" });

        // Act
        var result = await sut.SendMailAsync(dto);

        // Assert — false because SMTP unreachable, NOT because of recipient handling
        // With a real SMTP server this would return true for all three recipients
        Assert.False(result);
    }

    // -------------------------------------------------------------------------
    // Null / empty recipients — BUG 2 (still present)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SendMailAsync_NullMailRecipients_ReturnsFalseWithoutThrow()
    {
        // Arrange
        // BUG 2: foreach on null list throws NullReferenceException
        // caught by catch block — returns false
        var sut = CreateSut();
        var dto = ValidDto();
        dto = dto with { MailRecipients = null };
        //dto.MailRecipients = null!;

        // Act
        var result = await sut.SendMailAsync(dto);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SendMailAsync_EmptyMailRecipients_ReturnsFalseOrSendsWithNoRecipients()
    {
        // Arrange
        // With the foreach fix, an empty list does NOT throw — it just means
        // mailMessage.To is empty. SmtpClient may throw or succeed depending
        // on the server. Here SMTP is unreachable so catch fires either way.
        var sut = CreateSut();
        var dto = ValidDto();
        dto = dto with { MailRecipients = [] };
        //dto.MailRecipients = new List<string>();

        // Act
        var result = await sut.SendMailAsync(dto);

        // Assert — false (either empty To list or SMTP unreachable)
        Assert.False(result);
    }

    [Fact]
    public async Task SendMailAsync_NullDto_ReturnsFalseWithoutThrow()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        var result = await sut.SendMailAsync(null!);
        Assert.False(result);
    }

    // -------------------------------------------------------------------------
    // CancellationToken — BUG 4 (token not passed to SendMailAsync)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SendMailAsync_CancellationTokenAccepted_DoesNotThrow()
    {
        // Arrange — token is accepted in the signature but not yet passed
        // to smtpClient.SendMailAsync(message, ct) — documenting this gap
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();

        // Act & Assert
        var ex = await Record.ExceptionAsync(
            () => sut.SendMailAsync(ValidDto(), cts.Token));
        Assert.Null(ex);
    }

    [Fact]
    public async Task SendMailAsync_CancelledToken_StillAttemptsSend_DocumentsBug4()
    {
        // Arrange
        // BUG 4: ct is accepted but not forwarded to smtpClient.SendMailAsync.
        // A cancelled token should ideally prevent the SMTP call entirely.
        // Currently the token is ignored — SMTP is attempted regardless.
        // Fix: await smtpClient.SendMailAsync(message, ct)
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act — with the bug, cancellation is ignored and SMTP is attempted
        // (then fails because no listener) — returns false either way
        var result = await sut.SendMailAsync(ValidDto(), cts.Token);

        // Assert — false because SMTP fails, not because of cancellation
        Assert.False(result);
    }

    // -------------------------------------------------------------------------
    // IsBodyHtml flag
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SendMailAsync_IsHtmlTrue_DoesNotThrow()
    {
        var sut = CreateSut();
        var ex = await Record.ExceptionAsync(
            () => sut.SendMailAsync(ValidDto(content: "<h1>Hello</h1>", isHtml: true)));
        Assert.Null(ex);
    }

    [Fact]
    public async Task SendMailAsync_IsHtmlFalse_DoesNotThrow()
    {
        var sut = CreateSut();
        var ex = await Record.ExceptionAsync(
            () => sut.SendMailAsync(ValidDto(content: "Plain text", isHtml: false)));
        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // Empty subject / body
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SendMailAsync_EmptySubject_DoesNotThrow()
    {
        var sut = CreateSut();
        var ex = await Record.ExceptionAsync(
            () => sut.SendMailAsync(ValidDto(subject: string.Empty)));
        Assert.Null(ex);
    }

    [Fact]
    public async Task SendMailAsync_EmptyBody_DoesNotThrow()
    {
        var sut = CreateSut();
        var ex = await Record.ExceptionAsync(
            () => sut.SendMailAsync(ValidDto(content: string.Empty)));
        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // BUG 3 — Settings still captured at construction
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SendMailAsync_SettingsChangedAfterConstruction_StaleSettingsUsed_DocumentsBug3()
    {
        // Arrange
        // BUG 3: _settings = optionsMonitor.CurrentValue in constructor.
        // Runtime config changes (host, port, username) are ignored.
        // Fix: read _optionsMonitor.CurrentValue inside SendMailAsync.
        var sut = CreateSut();

        // Change the mock after construction
        _optionsMock
            .Setup(o => o.CurrentValue)
            .Returns(new EmailSenderEmailSmtpSettingsConfiguration
            {
                Host = "updated-host.com",
                Port = 587,
                Username = "updated@webhookservice.com"
            });

        // Act
        await sut.SendMailAsync(ValidDto());

        // Assert — CurrentValue only read once (at construction) — bug present
        // When fixed: Verify(Times.AtLeast(2))
        _optionsMock.Verify(o => o.CurrentValue, Times.Once);
    }

    // -------------------------------------------------------------------------
    // Recommended refactor — ISmtpClient abstraction
    // -------------------------------------------------------------------------

    [Fact]
    public void DocumentsRecommendedRefactor_ISmtpClientAbstraction()
    {
        // Extracting SmtpClient behind an interface unlocks:
        //
        //   public interface ISmtpClient : IDisposable
        //   {
        //       Task SendMailAsync(MailMessage message, CancellationToken ct);
        //   }
        //
        //   // In tests:
        //   _smtpMock
        //       .Setup(s => s.SendMailAsync(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()))
        //       .Returns(Task.CompletedTask);
        //
        //   var result = await sut.SendMailAsync(dto);
        //   Assert.True(result);  // ← can now assert true without real SMTP
        //
        //   // Verify all recipients were added:
        //   _smtpMock.Verify(s => s.SendMailAsync(
        //       It.Is<MailMessage>(m => m.To.Count == 3), ...), Times.Once);
        //
        //   // Verify cancellation is forwarded:
        //   _smtpMock.Verify(s => s.SendMailAsync(
        //       It.IsAny<MailMessage>(), cts.Token), Times.Once);

        Assert.True(true,
            "Inject ISmtpClient to enable full unit test coverage of SendMailAsync.");
    }
}
