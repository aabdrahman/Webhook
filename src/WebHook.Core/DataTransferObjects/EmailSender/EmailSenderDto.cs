namespace WebHook.Core.DataTransferObjects.EmailSender;

/// <summary>
/// An email message queued for sending by the email processor worker.
/// </summary>
/// <param name="MailContent">The message body, as plain text or HTML depending on <paramref name="IsHtml"/>.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="MailRecipients">The recipient email addresses.</param>
/// <param name="IsHtml">Whether <paramref name="MailContent"/> is HTML. Defaults to plain text.</param>
public record EmailSenderDto
(
    string MailContent,
    string Subject,
    List<string> MailRecipients,
    bool IsHtml = false
);
