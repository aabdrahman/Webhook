using WebHook.Core.DataTransferObjects.EmailSender;

namespace WebHook.Core.Interfaces.Helpers;

/// <summary>
/// Sends email over SMTP. Callers normally queue emails through
/// <see cref="EventContracts.Publishers.IApplicationPublisher.QueueEmailEvent"/> instead
/// of calling this directly.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email immediately.
    /// </summary>
    /// <param name="emailSenderItem">The email to send.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> if the SMTP server accepted the message; otherwise <see langword="false"/>.</returns>
    Task<bool> SendMailAsync(EmailSenderDto emailSenderItem, CancellationToken ct = default);
    //Task<bool> QueueEmailAsync(EmailSenderDto emailSenderItem, CancellationToken ct = default);
}
