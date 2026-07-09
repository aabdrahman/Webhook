using Serilog;
using System.Threading.Channels;
using WebHook.Core.EventContracts.Events;
using WebHook.Core.EventContracts.Publishers;

namespace WebHook.Infrastructure.EventPublishers;

public sealed class ApplicationPublisher : IApplicationPublisher
{
    private readonly Channel<EventRaised> _eventRaisedChannel;

    public ApplicationPublisher(Channel<EventRaised> eventRaisedChannel)
    {
        _eventRaisedChannel = eventRaisedChannel;
        _logger = Log.ForContext("ClassName", nameof(ApplicationPublisher));
    }

    private ILogger _logger;

    public async Task QueueEventRaised(EventRaised eventRaised, CancellationToken ct = default)
    {
        if(await _eventRaisedChannel.Writer.WaitToWriteAsync())
        {
            await _eventRaisedChannel.Writer.WriteAsync(eventRaised, ct);

            _logger.ForContext("MethodName", nameof(EventRaised)).Information("Event Raised Item Queued successfully - {0}", eventRaised);
            
        }
    }
}
