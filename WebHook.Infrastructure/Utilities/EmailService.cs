using Microsoft.Extensions.Options;
using Serilog;
using System.Net;
using System.Net.Mail;
using WebHook.Core.DataTransferObjects.EmailSender;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.Interfaces.Helpers;

namespace WebHook.Infrastructure.Utilities;

public class EmailService : IEmailService
{
    private readonly EmailSenderEmailSmtpSettingsConfiguration _settings;

    public EmailService(IOptionsMonitor<EmailSenderEmailSmtpSettingsConfiguration> optionsMonitor)
    {
        _settings = optionsMonitor.CurrentValue;
        _logger = Log.ForContext("ClassName", nameof(EmailService));
    }

    private ILogger _logger;

    public async Task<bool> SendMailAsync(EmailSenderDto emailSenderItem)
    {
        _logger = _logger.ForContext("MethodName", nameof(SendMailAsync));

        try
        {
            _logger.Information("Sending mail with details - {0}", emailSenderItem);

            string? smtpPassword = Environment.GetEnvironmentVariable("SmtpClientPassword");

            if (string.IsNullOrEmpty(smtpPassword))
            {
                _logger.Error("An errror occurred while sending email. No password has been profiled in the environment for send mail.");
                return false;
            }

            using var smtpClient = new SmtpClient(host: _settings.Host, port: _settings.Port)
            {
                Credentials = new NetworkCredential(userName: _settings.Username, password: smtpPassword),
                EnableSsl = true
            };

            using var mailMessage = new MailMessage
            {
                From = new MailAddress(_settings.Username),
                Subject = emailSenderItem.Subject,
                Body = emailSenderItem.MailContent,
                IsBodyHtml = emailSenderItem.IsHtml
            };

            foreach (var recipient in emailSenderItem.MailRecipients)
            {
                mailMessage.To.Add(recipient);
            }

            await smtpClient.SendMailAsync(message: mailMessage);
            _logger.Information("Mail sent successfully....");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while sending mail.");
            return false;
        }
    }
}
