using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using System.Threading.Channels;
using WebHook.Core.Constants;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.EventContracts.Events;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.BackgroundWorkers;

public class EventRaisedWorker : BackgroundService
{
    private readonly Channel<EventRaised> _eventRaisedChannel;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly EventRaisedWorkerConfiguration _eventRaisedWorkerConfiguration;

    public EventRaisedWorker(Channel<EventRaised> eventRaisedChannel, IServiceScopeFactory serviceScopeFactory, IOptionsMonitor<EventRaisedWorkerConfiguration> optionsMonitor)
    {
        _eventRaisedChannel = eventRaisedChannel;
        _serviceScopeFactory = serviceScopeFactory;
        _eventRaisedWorkerConfiguration = optionsMonitor.CurrentValue;
        _logger = Log.ForContext(_className, nameof(EventRaisedWorker));
    }

    private static List<EventRaised> _unsuccessfulRequests = new List<EventRaised>();

    private ILogger _logger;
    private const string _className = "ClassName";
    private const string _methodName = "MethodName";
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger = _logger.ForContext(_methodName, nameof(ExecuteAsync));

        using var timespanTimer = new PeriodicTimer(TimeSpan.FromSeconds(_eventRaisedWorkerConfiguration.ProcessingIntervalInSeconds));

        while (!stoppingToken.IsCancellationRequested && await timespanTimer.WaitForNextTickAsync(stoppingToken))
        {
            _logger.Information("Begin processing raised event channels....");

            await foreach (var item in _eventRaisedChannel.Reader.ReadAllAsync(stoppingToken))
            {
                _logger.Information("Begin processing raised event - {0}", item.createdEventId);
                await using var scope = _serviceScopeFactory.CreateAsyncScope();
                var repositoryContext = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

                //The transaction is started here, this ensures that the for update query which is run to select the item is successful.
                var transaction = await repositoryContext.Database.BeginTransactionAsync(stoppingToken);

                try
                {
                    await Task.Delay(10);
                    //The query runs, and lock the selected row for update. This ensures that another process does not pick it up and make same update.
                    WebhookEvent? eventItem = await repositoryContext.WebhookEvents.FromSqlRaw
                    (
                        @"SELECT * FROM ""WebhookEvents"" WHERE ""Id"" = {0} AND ""Status"" = {1} FOR UPDATE SKIP LOCKED", item.createdEventId, WebHookEventStatus.Pending.ToString()

                    ).FirstOrDefaultAsync();

                    //If ecent item is null, maybe the status has changed or its been updated by another worker, then, we return and continue with other raised events in the channel
                    if (eventItem is null)
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

                    //Validates if there is any subscriber to teh raised event.
                    if (subscribers.Any())
                    {
                        await repositoryContext.WebhookDeliveries.AddRangeAsync(subscribers, stoppingToken);

                        //Sets the status of the event item to processing and the processed at to current timestamp.
                        eventItem.Status = WebHookEventStatus.Processing;
                    }
                    else
                    {
                        _logger.Information("No downstream subscriber for the event - {0}", eventItem.EventType);
                        //Since no event, we will mark it as a processed event
                        eventItem.Status = WebHookEventStatus.Processed;

                    }

                    eventItem.ProcessedAt = DateTimeOffset.UtcNow;

                    try
                    {
                        await repositoryContext.SaveChangesAsync(stoppingToken);
                        await transaction.CommitAsync(stoppingToken);
                        _logger.Information("Event raised successfully processed - {0}", item.createdEventId);
                    }
                    catch (Exception ex)
                    {
                        //An exception occurred when performing the insert and update, hence a rollback is done and the item is transferred into the unsuccessful list item to re-enqueue
                        await transaction.RollbackAsync();
                        _unsuccessfulRequests.Add(item);
                        _logger.Error(ex, "An error occurred while making saving changes for processing items - {0}", item.createdEventId);
                        break;

                    }

                }
                catch (Exception ex)
                {
                    //A general exception occurred and the item is re-queued in channel
                    _unsuccessfulRequests.Add(item);
                    //await _eventRaisedChannel.Writer.WriteAsync(item, stoppingToken);
                    _logger.Error(ex, "An error occurred fetching the created event - {0} from database.", item.createdEventId);
                    break;
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

                        _unsuccessfulRequests.Clear();

                    }
                }
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
