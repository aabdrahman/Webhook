namespace WebHook.Infrastructure.Utilities;

/// <summary>
/// The kinds of notification email, each with its own HTML template.
/// </summary>
public enum NotificationType
{
    /// <summary>Tells a subscription's contact that a delivery was moved to the dead-letter queue.</summary>
    DeadLetterNotification,
    /// <summary>Tells a subscription's contact that their endpoint responded slower than the threshold.</summary>
    SlowEndpointNotification,
    /// <summary>Sends a user their one-time password.</summary>
    SendOtpNotification
}
