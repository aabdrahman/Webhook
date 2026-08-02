using WebHook.Core.DataTransferObjects.EmailSender;

namespace WebHook.Core.Interfaces.Helpers;

public interface IEmailService
{
    Task<bool> SendMailAsync(EmailSenderDto emailSenderItem);
}
