namespace WebHook.Core.Entities.ConfigurationModels;

public class EmailSenderEmailSmtpSettingsConfiguration
{
    public string Host { get; set; }
    public int Port { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
}
