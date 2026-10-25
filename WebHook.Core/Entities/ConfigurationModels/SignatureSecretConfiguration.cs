namespace WebHook.Core.Entities.ConfigurationModels;

/// <summary>
/// Settings for generating subscription signing secrets. Bound from configuration.
/// </summary>
public class SignatureSecretConfiguration
{
    /// <summary>
    /// The number of random bytes in each generated secret. Larger values give stronger secrets.
    /// </summary>
    public int KeySize { get; set; }
}
