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
    private readonly Channel<SubscriptionVerificationRaised> _subscriptionVerificationChannel;

    /// <summary>
    /// Initialises a new instance of the <see cref="ApplicationPublisher"/> class.
    /// </summary>
    /// <param name="eventRaisedChannel">The channel read by the event fan-out worker.</param>
    /// <param name="emailSenderChannel">The channel read by the email processor worker.</param>
    /// <param name="applicationCacheChannel">The channel read by the cache persistence worker.</param>
    /// <param name="subscriptionVerificationChannel">The channel read by the signature verification worker.</param>
    public ApplicationPublisher(Channel<EventRaised> eventRaisedChannel, Channel<EmailSenderDto> emailSenderChannel, Channel<ApplicationCacheEvent> applicationCacheChannel, Channel<SubscriptionVerificationRaised> subscriptionVerificationChannel)
    {
        _eventRaisedChannel = eventRaisedChannel;
        _emailSenderChannel = emailSenderChannel;
        _applicationCacheChannel = applicationCacheChannel;
        _subscriptionVerificationChannel = subscriptionVerificationChannel;

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

    /// <inheritdoc />
    /// <remarks>The cache channel is bounded (500 items), so the call waits for space when the worker is behind.</remarks>
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

    public async Task<bool> QueueSignatureVerificationEvent(SubscriptionVerificationRaised verificationRaised, CancellationToken ct = default)
    {
        if(await _subscriptionVerificationChannel.Writer.WaitToWriteAsync(ct))
        {
            await _subscriptionVerificationChannel.Writer.WriteAsync(verificationRaised, ct);
            _logger.ForContext("MethodName", nameof(QueueSignatureVerificationEvent)).Information("Signature verification item queued successfully - {0}", verificationRaised);
            return true;
        }

        _logger.ForContext("MethodName", nameof(QueueSignatureVerificationEvent)).Information("Signature verification item could not be queued - {0}", verificationRaised);
        return false;
    }
}
