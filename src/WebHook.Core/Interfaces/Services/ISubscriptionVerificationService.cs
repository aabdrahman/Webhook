using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.SubscriptionVerification;

namespace WebHook.Core.Interfaces.Services;

/// <summary>
/// Verifies that a subscriber controls and can correctly answer its callback URL before the subscription
/// receives deliveries.
/// </summary>
public interface ISubscriptionVerificationService
{
    /// <summary>
    /// Starts the callback URL handshake for an <c>Inactive</c> subscription the caller created. The handshake
    /// itself runs in the background; use <see cref="GetSubscriptionVerifications"/> to see the outcome.
    /// </summary>
    /// <param name="SubscriptionId">The subscription to verify.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A response whose status code reports whether the verification was started.</returns>
    Task<GenericResponse<string>> VerifySubscriptionCallbackUrl(Guid SubscriptionId, CancellationToken ct = default);

    /// <summary>
    /// Lists the verification attempts for a subscription, newest first.
    /// </summary>
    /// <param name="SubscriptionId">The subscription whose attempts to list.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The attempts with their status and timestamps, or an error response.</returns>
    Task<GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>> GetSubscriptionVerifications(Guid SubscriptionId, CancellationToken ct = default);
}
