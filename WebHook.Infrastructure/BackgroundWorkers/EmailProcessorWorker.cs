using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using System.Threading.Channels;
using WebHook.Core.DataTransferObjects.EmailSender;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.Interfaces.Helpers;

namespace WebHook.Infrastructure.BackgroundWorkers;

public class EmailProcessorWorker : BackgroundService
{
    private readonly Channel<EmailSenderDto> _emailSenderChannel;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly EmailProcessorWorkerConfiguration _emailProcessorWorkerConfiguration;

    public EmailProcessorWorker(Channel<EmailSenderDto> emailSenderChannel, IServiceScopeFactory serviceScopeFactory, IOptionsMonitor<EmailProcessorWorkerConfiguration> optionsMonitor)
    {
        _emailSenderChannel = emailSenderChannel;
        _serviceScopeFactory = serviceScopeFactory;
        _emailProcessorWorkerConfiguration = optionsMonitor.CurrentValue;

        _logger = Log.ForContext("ClassName", nameof(EmailProcessorWorker));
    }

    private ILogger _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger = _logger.ForContext("MethodName", nameof(ExecuteAsync));

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_emailProcessorWorkerConfiguration.ProcessingIntervalInSeconds));

        while(!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
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

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Information("Email processor worker is starting.....");
        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger = _logger.ForContext("MethodName", nameof(StopAsync));

        _logger.Information("Email sender worker is stopping....");

        if(_emailSenderChannel.Reader.Count > 0)
        {
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
