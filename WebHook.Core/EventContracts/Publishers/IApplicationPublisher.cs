using WebHook.Core.EventContracts.Events;

namespace WebHook.Core.EventContracts.Publishers;

public interface IApplicationPublisher
{
    Task QueueEventRaised(EventRaised @eventRaised, CancellationToken ct = default);
}
