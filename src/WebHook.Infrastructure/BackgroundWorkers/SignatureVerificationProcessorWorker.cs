using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using System.Threading.Channels;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.EventContracts.Events;
using WebHook.Infrastructure.Services;

namespace WebHook.Infrastructure.BackgroundWorkers;

public class SignatureVerificationProcessorWorker : BackgroundService
{
    private readonly Channel<SubscriptionVerificationRaised> _subscriptionVerififcationChannel;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly EventRaisedWorkerConfiguration _eventRaisedWorkerConfiguration;

    public SignatureVerificationProcessorWorker(Channel<SubscriptionVerificationRaised> subscriptionVerififcationChannel, IServiceScopeFactory serviceScopeFactory, IOptionsMonitor<EventRaisedWorkerConfiguration> eventRaisedWorkerConfiguration)
    {
        _subscriptionVerififcationChannel = subscriptionVerififcationChannel;
        _serviceScopeFactory = serviceScopeFactory;
        _eventRaisedWorkerConfiguration = eventRaisedWorkerConfiguration.CurrentValue;
    }

    private ILogger _logger = Log.ForContext("ClassName", nameof(SignatureVerificationProcessorWorker));
    private const string _methodName = "MethodName";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger = _logger.ForContext(_methodName, nameof(ExecuteAsync));

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_eventRaisedWorkerConfiguration.ProcessingIntervalInSeconds));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            _logger.Information("Begin processing subscription verification events channel.....");

            await foreach (var verificationEvent in _subscriptionVerififcationChannel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    //Create Scope for the signature verification processor service to process the event
                    using var scope = _serviceScopeFactory.CreateScope();
                    //Fetch the signature verification processor service from the scope and process the event
                    var signatureVerificationProcessorService = scope.ServiceProvider.GetRequiredService<SubscriptionVerificationProcessorService>();
                    //Process the signature verification event using the service
                    await signatureVerificationProcessorService.ProcessSignatureVerificationAsync(verificationEvent.VerificationId, stoppingToken);
                }
                catch (Exception ex)
                {
                    //Catch and log any exception that occurs while processing the subscription verification event
                    _logger.Error(ex, "An error occurred while processing subscription verification event - {0}", verificationEvent);
                }
            }
            
        }
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.ForContext(_methodName, nameof(StartAsync)).Information("Starting SignatureVerificationProcessorWorker with configuration - {0}", _eventRaisedWorkerConfiguration);
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.ForContext(_methodName, nameof(StopAsync)).Information("Stopping SignatureVerificationProcessorWorker");
        return base.StopAsync(cancellationToken);
    }
}
