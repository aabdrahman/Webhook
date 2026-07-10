using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Threading.Channels;
using WebHook.Core.EventContracts.Events;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.BackgroundWorkers;

public sealed class PendingRaisedEventsWorker : BackgroundService
{
    private readonly Channel<EventRaised> _eventRaisedChannel;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly TimeSpan _timeout;

    public PendingRaisedEventsWorker(Channel<EventRaised> eventRaisedChannel, IServiceScopeFactory serviceScopeFactory)
    {
        _eventRaisedChannel = eventRaisedChannel;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = Log.ForContext(_className, nameof(PendingRaisedEventsWorker));
        _timeout = TimeSpan.FromSeconds(300);
    }

    private ILogger _logger;
    private const string _className = "ClassName";
    private const string _methodName = "MethodName";

    private PeriodicTimer _timer;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger = _logger.ForContext(_methodName, nameof(ExecuteAsync));
        _timer = new PeriodicTimer(_timeout);

        while (!stoppingToken.IsCancellationRequested && await _timer.WaitForNextTickAsync(stoppingToken))
        {
            _logger.Information("Begin processing pending events.........");

            using var scope = _serviceScopeFactory.CreateAsyncScope();
            var repositoryContext = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

            DateTimeOffset thresholdDatetime = DateTimeOffset.UtcNow.AddMinutes(-30);

            List<Guid> pendingIds = await repositoryContext.WebhookEvents
                                        .Where(x => x.Status == Core.Constants.WebHookEventStatus.Pending && x.CreatedAt <= thresholdDatetime)
                                        .Select(x => x.Id)
                                        .ToListAsync(stoppingToken);

            if (!pendingIds.Any())
            {
                _logger.Information("No extra pending events yet to be processed....");
                continue;
            }

            foreach (var pending in pendingIds)
            {
                await _eventRaisedChannel.Writer.WriteAsync(new EventRaised(pending), stoppingToken);
            }

            _logger.Information("Events pushed successfully to channel for processing - {0}", pendingIds);

        }

        await Task.Delay(10000);
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        
        _logger.ForContext(_methodName, nameof(StartAsync)).Information("Pending raised webhook events worker started successfully.....");
        await base.StartAsync(cancellationToken);
    }
}
