using System.Text;
using Microsoft.AspNetCore.Hosting;
using Serilog;

namespace WebHook.Infrastructure.Utilities;

public class EmailContentFormatterHelper
{
    private readonly IWebHostEnvironment _environment;

    public EmailContentFormatterHelper(IWebHostEnvironment environment)
    {
        _environment = environment;
        _logger = Log.ForContext("ClassName", nameof(EmailContentFormatterHelper));
    }

    private ILogger _logger;

    private static readonly IReadOnlyDictionary<NotificationType, string> emailTemplates = new Dictionary<NotificationType, string>()
    {
        { NotificationType.DeadLetterNotification, "DeadLetterNotification.html" },
        { NotificationType.SlowEndpointNotification, "SlowEndpointNotification.html" }
    };

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

            var staticFilePath = _environment.ContentRootPath;

            var templateFilepath = Path.Combine(staticFilePath, "EmailNotificationTemplates", emailTemplateFilename);

            if (!File.Exists(templateFilepath))
            {
                _logger.Warning("The notification type email template file does not exist - {0}. File name - {1}", emailType, emailTemplateFilename);
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
