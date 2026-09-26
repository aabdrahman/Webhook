using Serilog;
using System.Threading.Channels;
using WebHook.Core.DataTransferObjects.EmailSender;
using WebHook.Core.EventContracts.Events;
using WebHook.Core.EventContracts.Publishers;

namespace WebHook.Infrastructure.EventPublishers;

public sealed class ApplicationPublisher : IApplicationPublisher
{
    private readonly Channel<EventRaised> _eventRaisedChannel;
    private readonly Channel<EmailSenderDto> _emailSenderChannel;

    public ApplicationPublisher(Channel<EventRaised> eventRaisedChannel, Channel<EmailSenderDto> emailSenderChannel)
    {
        _eventRaisedChannel = eventRaisedChannel;
        _emailSenderChannel = emailSenderChannel;
        _logger = Log.ForContext("ClassName", nameof(ApplicationPublisher));
    }

    private ILogger _logger;

    public async Task QueueEventRaised(EventRaised eventRaised, CancellationToken ct = default)
    {
        if (await _eventRaisedChannel.Writer.WaitToWriteAsync())
        {
            await _eventRaisedChannel.Writer.WriteAsync(eventRaised, ct);

            _logger.ForContext("MethodName", nameof(EventRaised)).Information("Event Raised Item Queued successfully - {0}", eventRaised);

        }
    }

    public async Task<bool> QueueEmailEvent(EmailSenderDto emailSenderItem, CancellationToken ct = default)
    {
        if(await _emailSenderChannel.Writer.WaitToWriteAsync(ct))
        {
            await _emailSenderChannel.Writer.WriteAsync(emailSenderItem, ct);
            _logger.ForContext("MethodName", nameof(QueueEmailEvent)).Information("Email item queued successfully - {0}", emailSenderItem);
            return true;
        }

        _logger.ForContext("MethodName", nameof(QueueEmailEvent)).Information("Email item could not be queued - {0}", emailSenderItem);
        return false;
    }
}
