using Microsoft.AspNetCore.Hosting;
using Serilog;
using System.Text;

namespace WebHook.Infrastructure.Utilities;

/// <summary>
/// Renders notification emails from the HTML templates in
/// <c>wwwroot/StaticFiles/EmailNotificationTemplates</c>.
/// </summary>
public class EmailContentFormatterHelper
{
    private readonly IWebHostEnvironment _environment;
    private readonly IHostingEnvironment _hostingEnvironment;

    /// <summary>
    /// Initialises a new instance of the <see cref="EmailContentFormatterHelper"/> class.
    /// </summary>
    /// <param name="environment">The web host environment.</param>
    /// <param name="hostingEnvironment">The hosting environment, used for the content root path.</param>
    public EmailContentFormatterHelper(IWebHostEnvironment environment, IHostingEnvironment hostingEnvironment)
    {
        _environment = environment;
        _hostingEnvironment = hostingEnvironment;
        _logger = Log.ForContext("ClassName", nameof(EmailContentFormatterHelper));
        
    }

    private ILogger _logger;

    private static readonly IReadOnlyDictionary<NotificationType, string> emailTemplates = new Dictionary<NotificationType, string>()
    {
        { NotificationType.DeadLetterNotification, "DeadLetterNotification.html" },
        { NotificationType.SlowEndpointNotification, "SlowEndpointNotification.html" },
        { NotificationType.SendOtpNotification, "OtpNotification.html" }
    };

    /// <summary>
    /// Loads the template for a notification type and replaces each <c>{{Key}}</c> placeholder
    /// with its value.
    /// </summary>
    /// <remarks>Values are inserted as-is, without HTML encoding.</remarks>
    /// <param name="emailType">The notification type, which selects the template.</param>
    /// <param name="parameters">Placeholder names (without braces) mapped to their values.</param>
    /// <returns>The rendered HTML, or an empty string if the template is missing or an error occurs.</returns>
    public async Task<string?> GetEmailContentAsync(NotificationType emailType, Dictionary<string, string> parameters)
    {
        _logger = _logger.ForContext("MethodName", nameof(GetEmailContentAsync));

        if (parameters is null)
        {
            _logger.Warning("Parameters dictionary is null for notification type {EmailType}.", emailType);
            return string.Empty;
        }

        try
        {
            if (!emailTemplates.TryGetValue(emailType, out var emailTemplateFilename))
            {
                _logger.Warning("No email template is configured for the notification type - {0}", emailType.ToString());
                return string.Empty;
            }

            var staticFilePath = _hostingEnvironment.ContentRootPath;

            var templateFilepath = Path.Combine(staticFilePath, "wwwroot", "StaticFiles", "EmailNotificationTemplates", emailTemplateFilename);

            if (!File.Exists(templateFilepath))
            {
                _logger.Warning("The notification type email template file does not exist - {0}. File name - {1}, at path - {2}", emailType, emailTemplateFilename, templateFilepath);
                return string.Empty;
            }

            var templateFileText = new StringBuilder();
            templateFileText.Append(await File.ReadAllTextAsync(templateFilepath));

            foreach (var parameter in parameters)
            {
                string textToReplace = string.Concat("{{", parameter.Key, "}}");
                templateFileText.Replace(textToReplace, parameter.Value);
            }

            return templateFileText.ToString();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while geting the email content for notification type - {0}", emailType);
            return string.Empty;
        }
    }
}
