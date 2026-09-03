namespace WebHook.Core.Entities.ConfigurationModels;

public class OtpSettingsConfiguration
{
    public int MaximumOtpLength { get; set; }
    public int OtpToGenerateLength { get; set; }
}
