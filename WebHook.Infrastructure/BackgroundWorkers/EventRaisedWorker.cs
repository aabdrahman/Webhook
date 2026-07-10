using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Threading.Channels;
using WebHook.Core.Constants;
using WebHook.Core.Entities;
using WebHook.Core.EventContracts.Events;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.BackgroundWorkers;

public sealed class EventRaisedWorker : BackgroundService
{
    private readonly Channel<EventRaised> _eventRaisedChannel;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public EventRaisedWorker(Channel<EventRaised> eventRaisedChannel, IServiceScopeFactory serviceScopeFactory)
    {
        _eventRaisedChannel = eventRaisedChannel;
        _logger = Log.ForContext(_className, nameof(EventRaisedWorker));
        _serviceScopeFactory = serviceScopeFactory;
    }

    private static List<EventRaised> _unsuccessfulRequests = new List<EventRaised>();

    private ILogger _logger;
    private const string _className = "ClassName";
    private const string _methodName = "MethodName";
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger = _logger.ForContext(_methodName, nameof(ExecuteAsync));

        var timespanTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));
    

        while (!stoppingToken.IsCancellationRequested)
        {
            if(await timespanTimer.WaitForNextTickAsync(stoppingToken))
            {
                _logger.Information("Begin processing raised event channels....");

                await foreach (var item in _eventRaisedChannel.Reader.ReadAllAsync(stoppingToken))
                {
                    _logger.Information("Begin processing raised event - {0}", item.createdEventId);
                    using var scope = _serviceScopeFactory.CreateAsyncScope();
                    var repositoryContext = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

                    //The transaction is started here, this ensures that the for update query which is run to select the item is successful.
                    var transaction = await repositoryContext.Database.BeginTransactionAsync(stoppingToken);

                    try
                    {
                        await Task.Delay(1000);
                        //The query runs, and lock the selected row for update. This ensures that another process does not pick it up and make same update.
                        WebhookEvent? eventItem = await repositoryContext.WebhookEvents.FromSqlRaw
                        (
                            @"SELECT * FROM ""WebhookEvents"" WHERE ""Id"" = {0} AND ""Status"" = {1} FOR UPDATE SKIP LOCKED", item.createdEventId, WebHookEventStatus.Pending.ToString()

                        ).FirstOrDefaultAsync();

                        //If ecent item is null, maybe the status has changed or its been updated by another worker, then, we return and continue with other raised events in the channel
                        if(eventItem is null)
                        {
                            _logger.Information("Created Event with Id - {0} cannot be feched. Possibly currently processing", item.createdEventId);
                            await transaction.RollbackAsync(stoppingToken);
                            continue;
                        }

                        _logger.Information("Begin to fetch all event subscribed to the event type");

                        //We begin to genrate for all possible subscriptions. All subscriptions are then used to get a list of webhook deliveries for the event
                        List<WebhookDelivery> subscribers = await repositoryContext.WebhookEventSubscriptions
                                                                        .Include(x => x.webhookSubscription)
                                                                        .Where(x => x.webHookEventCatalog.NormalizedEventName == eventItem.EventType.ToUpper() && x.webhookSubscription.IsActive)
                                                                        .Select(x => new WebhookDelivery()
                                                                        {
                                                                            RetryCount = 0,
                                                                            CreatedAt = DateTimeOffset.UtcNow,
                                                                            CallBackUrl = x.webhookSubscription.CallbackUrl,
                                                                            DeliveryStatus = WebhookDeliveryStatus.Pending,
                                                                            WebhookEventId = eventItem.Id,
                                                                            WebhookSubscriptionEventId = x.Id,
                                                                            RequestPayload = eventItem.PayLoad
                                                                        })
                                                                        .ToListAsync(stoppingToken);
                        

                        if (subscribers.Any())
                        {
                            await repositoryContext.WebhookDeliveries.AddRangeAsync(subscribers, stoppingToken);
                        }
                        else
                        {
                            _logger.Information("No downstream subscriber for the event - {0}", eventItem.EventType);
                        }

                        eventItem.ProcessedAt = DateTimeOffset.UtcNow;
                        eventItem.Status = WebHookEventStatus.Processing;

                        try
                        {
                            await repositoryContext.SaveChangesAsync(stoppingToken);
                            await transaction.CommitAsync(stoppingToken);
                            _logger.Information("Event raised successfully processed - {0}", item.createdEventId);
                        }
                        catch (Exception ex)
                        {
                            //An exception occurred when performing the insert and update, ehnce a rollback is done and the item is transferred into the unsuccessful list item to re-enqueue
                            await transaction.RollbackAsync();
                            _unsuccessfulRequests.Add(item);
                            //await _eventRaisedChannel.Writer.WriteAsync(item);
                            _logger.Error(ex, "An error occurred while making saving changes for processing items - {0}", item.createdEventId);
                            continue;
                            
                        }

                    }
                    catch (Exception ex)
                    {
                        //A general exception occurred and the item is re-queued in channel
                        _unsuccessfulRequests.Add(item);
                        //await _eventRaisedChannel.Writer.WriteAsync(item, stoppingToken);
                        _logger.Error(ex, "An error occurred fetching the created event - {0} from database.", item.createdEventId);
                        continue; 
                    }
                    finally
                    {
                        //Here, after the whole operation, we check for any unsuccessful item to re-queue in channel
                        if (_unsuccessfulRequests.Any())
                        {
                            _logger.Information("Re queueing unsucccessful processed events....");
                            foreach (var unsuccessfulItem in _unsuccessfulRequests)
                            {
                                await _eventRaisedChannel.Writer.WriteAsync(unsuccessfulItem);
                            }
                            
                        }
                    }

                    await Task.Delay(500,stoppingToken);
                }

                await Task.Delay(2000, stoppingToken);
            }

            
        }
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger = _logger.ForContext(_methodName, nameof(StartAsync));

        _logger.Information("Starting event raised worker service - Initial Count: {0}", 0);

        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger = _logger.ForContext(_methodName, nameof(StopAsync));

        _logger.Information("Stopping event raised worker at - {0}. Total Unprocessed channel items - {1}", DateTimeOffset.UtcNow, 0);

        return base.StopAsync(cancellationToken);
    }
}
