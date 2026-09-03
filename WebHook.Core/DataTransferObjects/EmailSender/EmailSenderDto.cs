namespace WebHook.Core.DataTransferObjects.EmailSender;

public record EmailSenderDto
(
    string MailContent,
    string Subject,
    List<string> MailRecipients,
    bool IsHtml = false
);
