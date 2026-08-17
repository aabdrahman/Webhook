using Microsoft.AspNetCore.Hosting;
using Moq;
using Serilog;
using WebHook.Infrastructure.Utilities;

namespace WebHook.UnitTests.Utilities;

/// <summary>
/// Unit tests for <see cref="EmailContentFormatterHelper"/>.
///
/// Uses a real temporary directory with real template files rather than
/// mocking the file system — this tests the actual file reading and
/// placeholder substitution logic end to end without needing a web host.
/// </summary>
public sealed class EmailContentFormatterHelperTests : IDisposable
{
    // -------------------------------------------------------------------------
    // Setup / teardown
    // -------------------------------------------------------------------------

    private readonly string _tempDirectory;
    private readonly string _templateDirectory;
    private readonly Mock<IWebHostEnvironment> _environmentMock;
    private readonly EmailContentFormatterHelper _sut;

    public EmailContentFormatterHelperTests()
    {
        Log.Logger = new LoggerConfiguration().CreateLogger();

        // Create a real temp directory so File.Exists and File.ReadAllTextAsync work
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _templateDirectory = Path.Combine(_tempDirectory, "EmailNotificationTemplates");

        Directory.CreateDirectory(_templateDirectory);

        // Mock IWebHostEnvironment to point ContentRootPath at our temp dir
        _environmentMock = new Mock<IWebHostEnvironment>();
        _environmentMock
            .Setup(e => e.ContentRootPath)
            .Returns(_tempDirectory);

        _sut = new EmailContentFormatterHelper(_environmentMock.Object);
    }

    public void Dispose()
    {
        // Clean up temp directory after each test
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private string WriteTemplate(string fileName, string content)
    {
        var path = Path.Combine(_templateDirectory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static Dictionary<string, string> Params(params (string key, string value)[] pairs) =>
        pairs.ToDictionary(p => p.key, p => p.value);

    // -------------------------------------------------------------------------
    // Happy path — placeholder substitution
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetEmailContentAsync_ValidTemplate_ReturnsRenderedContent()
    {
        // Arrange
        WriteTemplate("DeadLetterNotification.html",
            "<p>Hello {{ContactName}}, your delivery {{DeliveryId}} failed.</p>");

        var parameters = Params(
            ("ContactName", "John Doe"),
            ("DeliveryId", "abc-123"));

        // Act
        var result = await _sut.GetEmailContentAsync(
            NotificationType.DeadLetterNotification, parameters);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("John Doe", result);
        Assert.Contains("abc-123", result);
        Assert.DoesNotContain("{{ContactName}}", result);
        Assert.DoesNotContain("{{DeliveryId}}", result);
    }

    [Fact]
    public async Task GetEmailContentAsync_MultipleParameters_AllReplacedCorrectly()
    {
        // Arrange
        WriteTemplate("DeadLetterNotification.html",
            "{{A}} {{B}} {{C}}");

        var parameters = Params(("A", "one"), ("B", "two"), ("C", "three"));

        // Act
        var result = await _sut.GetEmailContentAsync(
            NotificationType.DeadLetterNotification, parameters);

        // Assert
        Assert.Equal("one two three", result);
    }

    [Fact]
    public async Task GetEmailContentAsync_SamePlaceholderAppearsMultipleTimes_AllInstancesReplaced()
    {
        // Arrange — ContactName appears twice in the template
        WriteTemplate("DeadLetterNotification.html",
            "Dear {{ContactName}}, your contact is {{ContactName}}.");

        var parameters = Params(("ContactName", "Jane"));

        // Act
        var result = await _sut.GetEmailContentAsync(
            NotificationType.DeadLetterNotification, parameters);

        // Assert
        Assert.Equal("Dear Jane, your contact is Jane.", result);
        Assert.DoesNotContain("{{ContactName}}", result);
    }

    [Fact]
    public async Task GetEmailContentAsync_EmptyParameters_ReturnsTemplateWithPlaceholdersIntact()
    {
        // Arrange
        WriteTemplate("DeadLetterNotification.html",
            "<p>Hello {{ContactName}}</p>");

        // Act
        var result = await _sut.GetEmailContentAsync(
            NotificationType.DeadLetterNotification,
            new Dictionary<string, string>());

        // Assert — template returned as-is, placeholder untouched
        Assert.NotNull(result);
        Assert.Contains("{{ContactName}}", result);
    }

    [Fact]
    public async Task GetEmailContentAsync_SlowEndpointNotification_ReturnsRenderedContent()
    {
        // Arrange
        WriteTemplate("SlowEndpointNotification.html",
            "<p>{{SubscriptionName}} took {{ResponseTimeMs}}ms</p>");

        var parameters = Params(
            ("SubscriptionName", "My Webhook"),
            ("ResponseTimeMs", "8500"));

        // Act
        var result = await _sut.GetEmailContentAsync(
            NotificationType.SlowEndpointNotification, parameters);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("My Webhook", result);
        Assert.Contains("8500ms", result);
    }

    [Fact]
    public async Task GetEmailContentAsync_ParameterNotInTemplate_OtherParametersStillReplaced()
    {
        // Arrange — only ContactName is in the template, DeliveryId is not
        WriteTemplate("DeadLetterNotification.html",
            "Hello {{ContactName}}");

        var parameters = Params(
            ("ContactName", "John"),
            ("DeliveryId", "xyz-999")); // not in template

        // Act
        var result = await _sut.GetEmailContentAsync(
            NotificationType.DeadLetterNotification, parameters);

        // Assert — ContactName replaced, unknown key silently ignored
        Assert.NotNull(result);
        Assert.Equal("Hello John", result);
    }

    [Fact]
    public async Task GetEmailContentAsync_ParameterValueIsEmpty_PlaceholderReplacedWithEmpty()
    {
        // Arrange
        WriteTemplate("DeadLetterNotification.html",
            "Hello {{ContactName}}!");

        var parameters = Params(("ContactName", string.Empty));

        // Act
        var result = await _sut.GetEmailContentAsync(
            NotificationType.DeadLetterNotification, parameters);

        // Assert — empty string replaces the placeholder
        Assert.Equal("Hello !", result);
    }

    [Fact]
    public async Task GetEmailContentAsync_TemplateContainsHtml_HtmlPreserved()
    {
        // Arrange — full HTML structure preserved after substitution
        const string template = @"<!DOCTYPE html>
                            <html>
                            <body>
                              <h1>Hello {{ContactName}}</h1>
                              <p>Delivery <strong>{{DeliveryId}}</strong> failed.</p>
                            </body>
                            </html>";

        WriteTemplate("DeadLetterNotification.html", template);

        var parameters = Params(
            ("ContactName", "Abdrahman"),
            ("DeliveryId", "del-001"));

        // Act
        var result = await _sut.GetEmailContentAsync(
            NotificationType.DeadLetterNotification, parameters);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("<!DOCTYPE html>", result);
        Assert.Contains("<h1>Hello Abdrahman</h1>", result);
        Assert.Contains("<strong>del-001</strong>", result);
    }

    // -------------------------------------------------------------------------
    // Template not found
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetEmailContentAsync_TemplateFileDoesNotExist_ReturnsNull()
    {
        // Arrange — do NOT write the template file

        // Act
        var result = await _sut.GetEmailContentAsync(
            NotificationType.DeadLetterNotification,
            Params(("ContactName", "John")));

        // Assert
        Assert.True(string.IsNullOrEmpty(result));
    }

    [Fact]
    public async Task GetEmailContentAsync_TemplateDirectoryDoesNotExist_ReturnsNull()
    {
        // Arrange — point ContentRootPath to a directory with no templates folder
        var emptyRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(emptyRoot);

        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.ContentRootPath).Returns(emptyRoot);

        var sut = new EmailContentFormatterHelper(envMock.Object);

        try
        {
            // Act
            var result = await sut.GetEmailContentAsync(
                NotificationType.DeadLetterNotification,
                Params(("ContactName", "John")));

            // Assert
            Assert.True(string.IsNullOrEmpty(result));
        }
        finally
        {
            Directory.Delete(emptyRoot, recursive: true);
        }
    }

    // -------------------------------------------------------------------------
    // Unregistered notification type
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetEmailContentAsync_UnregisteredNotificationType_ReturnsNull()
    {
        // Arrange — cast an integer to NotificationType that has no template entry
        var unregisteredType = (NotificationType)999;

        // Act
        var result = await _sut.GetEmailContentAsync(
            unregisteredType,
            Params(("ContactName", "John")));

        // Assert
        Assert.True(string.IsNullOrEmpty(result));
    }

    // -------------------------------------------------------------------------
    // Null / invalid parameters
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetEmailContentAsync_NullParameters_ReturnsNull()
    {
        // Arrange
        WriteTemplate("DeadLetterNotification.html", "Hello {{ContactName}}");

        // Act
        var result = await _sut.GetEmailContentAsync(
            NotificationType.DeadLetterNotification,
            null!);

        // Assert — null parameters handled gracefully, not thrown
        Assert.True(string.IsNullOrEmpty(result));
    }

    // -------------------------------------------------------------------------
    // IWebHostEnvironment interaction
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetEmailContentAsync_ContentRootPathUsedToResolveTemplate()
    {
        // Arrange
        WriteTemplate("DeadLetterNotification.html", "Hello {{ContactName}}");

        // Act
        await _sut.GetEmailContentAsync(
            NotificationType.DeadLetterNotification,
            Params(("ContactName", "John")));

        // Assert — ContentRootPath was accessed to build the file path
        _environmentMock.Verify(e => e.ContentRootPath, Times.AtLeastOnce);
    }

    // -------------------------------------------------------------------------
    // Case sensitivity
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetEmailContentAsync_PlaceholderCaseMismatch_NotReplaced()
    {
        // Arrange — template has {{ContactName}}, parameter key is lowercase
        WriteTemplate("DeadLetterNotification.html", "Hello {{ContactName}}");

        var parameters = Params(("contactname", "John")); // wrong case

        // Act
        var result = await _sut.GetEmailContentAsync(
            NotificationType.DeadLetterNotification, parameters);

        // Assert — case-sensitive match means placeholder is NOT replaced
        Assert.NotNull(result);
        Assert.Contains("{{ContactName}}", result); // still there — case mismatch
    }

    // -------------------------------------------------------------------------
    // Concurrent calls — thread safety
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetEmailContentAsync_ConcurrentCalls_AllReturnCorrectContent()
    {
        // Arrange
        WriteTemplate("DeadLetterNotification.html", "Hello {{ContactName}}");

        // Act — fire 10 concurrent calls
        var tasks = Enumerable.Range(1, 10).Select(i =>
            _sut.GetEmailContentAsync(
                NotificationType.DeadLetterNotification,
                Params(("ContactName", $"User{i}"))));

        var results = await Task.WhenAll(tasks);

        // Assert — each call returns its own correctly rendered content
        for (int i = 0; i < results.Length; i++)
        {
            Assert.NotNull(results[i]);
            Assert.Contains($"User{i + 1}", results[i]!);
            Assert.DoesNotContain("{{ContactName}}", results[i]!);
        }
    }
}