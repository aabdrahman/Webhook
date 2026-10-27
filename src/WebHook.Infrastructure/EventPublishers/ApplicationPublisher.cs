using Serilog;
using System.Threading.Channels;
using WebHook.Core.DataTransferObjects.ApplicationCache;
using WebHook.Core.DataTransferObjects.EmailSender;
using WebHook.Core.EventContracts.Events;
using WebHook.Core.EventContracts.Publishers;

namespace WebHook.Infrastructure.EventPublishers;

/// <summary>
/// <see cref="IApplicationPublisher"/> implementation that writes to the application's
/// singleton <see cref="Channel{T}"/> instances.
/// </summary>
public sealed class ApplicationPublisher : IApplicationPublisher
{
    private readonly Channel<EventRaised> _eventRaisedChannel;
    private readonly Channel<EmailSenderDto> _emailSenderChannel;
    private readonly Channel<ApplicationCacheEvent> _applicationCacheChannel;

    /// <summary>
    /// Initialises a new instance of the <see cref="ApplicationPublisher"/> class.
    /// </summary>
    /// <param name="eventRaisedChannel">The channel read by the event fan-out worker.</param>
    /// <param name="emailSenderChannel">The channel read by the email processor worker.</param>
    public ApplicationPublisher(Channel<EventRaised> eventRaisedChannel, Channel<EmailSenderDto> emailSenderChannel, Channel<ApplicationCacheEvent> applicationCacheChannel)
    {
        _eventRaisedChannel = eventRaisedChannel;
        _emailSenderChannel = emailSenderChannel;
        _applicationCacheChannel = applicationCacheChannel;

        _logger = Log.ForContext("ClassName", nameof(ApplicationPublisher));
    }

    private ILogger _logger;

    /// <inheritdoc />
    public async Task QueueEventRaised(EventRaised eventRaised, CancellationToken ct = default)
    {
        if (await _eventRaisedChannel.Writer.WaitToWriteAsync())
        {
            await _eventRaisedChannel.Writer.WriteAsync(eventRaised, ct);

            _logger.ForContext("MethodName", nameof(EventRaised)).Information("Event Raised Item Queued successfully - {0}", eventRaised);

        }
    }

    /// <inheritdoc />
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

    public async Task<bool> QueueItemToCache(ApplicationCacheEvent cacheEvent, CancellationToken ct = default)
    {
        if(await _applicationCacheChannel.Writer.WaitToWriteAsync(ct))
        {
            await _applicationCacheChannel.Writer.WriteAsync(cacheEvent, ct);
            _logger.ForContext("MethodName", nameof(QueueItemToCache)).Information("Cache item queued successfully - {0}", cacheEvent);
            return true;
        }

        _logger.ForContext("MethodName", nameof(QueueItemToCache)).Information("Cache item could not be queued - {0}", cacheEvent);
        return false;
    }
}
