namespace WebHook.Core.Entities.ConfigurationModels;

/// <summary>
/// This is the JWT Settings configuration
/// Defines the jwt settings for the project.
/// </summary>
public class JwtSettingsConfiguration
{
    /// <summary>
    /// This defines the only valid issuer of the token for the project
    /// </summary>
    public string ValidIssuer { get; set; }
    /// <summary>
    /// This defines all possible audiences that can be issued token.
    /// This is also the audiences that can communicate successfully with the project system.
    /// All audiences are seperated by the identifier ';'
    /// </summary>
    public string ValidAudiences { get; set; }
    /// <summary>
    /// This defines the duration for which a token is meant to be valid for.
    /// </summary>
    public double TokenExpirationAfterInSeconds { get; set; }
    /// <summary>
    /// This defines the duration for which the refresh token is valid for.
    /// After the configured time, a user is  required to perform a fresh login.
    /// </summary>
    public double RefreshTokenExpirationAfterInSeconds { get; set; }
}