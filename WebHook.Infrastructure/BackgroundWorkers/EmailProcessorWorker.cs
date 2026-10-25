using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using System.Threading.Channels;
using WebHook.Core.DataTransferObjects.EmailSender;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.Interfaces.Helpers;

namespace WebHook.Infrastructure.BackgroundWorkers;

/// <summary>
/// Sends the emails queued through <see cref="Core.EventContracts.Publishers.IApplicationPublisher.QueueEmailEvent"/>.
/// </summary>
/// <remarks>
/// Each email is sent after a configurable delay, to throttle calls to the SMTP server.
/// Failed sends are logged and not retried. On shutdown, emails still in the channel are sent
/// before the worker stops.
/// </remarks>
public class EmailProcessorWorker : BackgroundService
{
    private readonly Channel<EmailSenderDto> _emailSenderChannel;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly EmailProcessorWorkerConfiguration _emailProcessorWorkerConfiguration;

    /// <summary>
    /// Initialises a new instance of the <see cref="EmailProcessorWorker"/> class.
    /// </summary>
    /// <param name="emailSenderChannel">The channel queued emails are read from.</param>
    /// <param name="serviceScopeFactory">Creates a scope, and so an <see cref="IEmailService"/>, per email.</param>
    /// <param name="optionsMonitor">The worker's interval and per-email delay, read once at construction.</param>
    public EmailProcessorWorker(Channel<EmailSenderDto> emailSenderChannel, IServiceScopeFactory serviceScopeFactory, IOptionsMonitor<EmailProcessorWorkerConfiguration> optionsMonitor)
    {
        _emailSenderChannel = emailSenderChannel;
        _serviceScopeFactory = serviceScopeFactory;
        _emailProcessorWorkerConfiguration = optionsMonitor.CurrentValue;

        _logger = Log.ForContext("ClassName", nameof(EmailProcessorWorker));
    }

    private ILogger _logger;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger = _logger.ForContext("MethodName", nameof(ExecuteAsync));

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_emailProcessorWorkerConfiguration.ProcessingIntervalInSeconds));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            _logger.Information("Begin processing email sending worker....");

            await foreach (var emailItem in _emailSenderChannel.Reader.ReadAllAsync(stoppingToken))
            {
                await Task.Delay(_emailProcessorWorkerConfiguration.ProcessingDelayInMilliSeconds, stoppingToken);
                using var scope = _serviceScopeFactory.CreateScope();
                var emailSenderService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                try
                {
                    var result = await emailSenderService.SendMailAsync(emailItem);

                    _logger.Information("Mail sending retuns - {0}", result);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "An error occurred while processing email sender in channel...");
                    continue;
                }
            }
        }
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.ForContext("MethodName", nameof(StartAsync)).Information("Email processor worker is starting.....");
        return base.StartAsync(cancellationToken);
    }

    /// <summary>
    /// Sends any emails left in the channel, one second apart, then stops the worker.
    /// </summary>
    /// <param name="cancellationToken">Signals that shutdown should no longer be graceful.</param>
    /// <returns>A task that completes when the worker has stopped.</returns>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger = _logger.ForContext("MethodName", nameof(StopAsync));

        _logger.Information("Email sender worker is stopping....");

        while (_emailSenderChannel.Reader.TryRead(out var emailItem))
        {
            await Task.Delay(1000, cancellationToken);
            using var scope = _serviceScopeFactory.CreateScope();
            var emailSenderService = scope.ServiceProvider.GetRequiredService<IEmailService>();
            try
            {
                var result = await emailSenderService.SendMailAsync(emailItem);

                _logger.Information("Mail sending returns - {0}", result);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "An error occurred while processing email sender in channel...");
                continue;
            }

            //await foreach (var emailItem in _emailSenderChannel.Reader.ReadAllAsync(cancellationToken))
            //{
            //    await Task.Delay(1000, cancellationToken);
            //    using var scope = _serviceScopeFactory.CreateScope();
            //    var emailSenderService = scope.ServiceProvider.GetRequiredService<IEmailService>();
            //    try
            //    {
            //        var result = await emailSenderService.SendMailAsync(emailItem);

            //        _logger.Information("Mail sending returns - {0}", result);
            //    }
            //    catch (Exception ex)
            //    {
            //        _logger.Error(ex, "An error occurred while processing email sender in channel...");
            //        continue;
            //    }
            //}
        }

        await base.StopAsync(cancellationToken);
    }
}
