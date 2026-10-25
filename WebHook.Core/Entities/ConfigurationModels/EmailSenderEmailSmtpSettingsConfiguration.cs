namespace WebHook.Core.Entities.ConfigurationModels;

/// <summary>
/// SMTP settings used by the email service. Bound from the <c>EmailSmtpSettings</c> configuration section.
/// </summary>
public class EmailSenderEmailSmtpSettingsConfiguration
{
    /// <summary>The SMTP server host name.</summary>
    public string Host { get; set; }
    /// <summary>The SMTP server port.</summary>
    public int Port { get; set; }
    /// <summary>The user name used to authenticate with the SMTP server.</summary>
    public string Username { get; set; }
    /// <summary>
    /// Not used: the email service reads the SMTP password from the <c>SmtpClientPassword</c>
    /// environment variable instead.
    /// </summary>
    public string Password { get; set; }
    /// <summary>The address emails are sent from.</summary>
    public string SenderEmail { get; set; }
}
