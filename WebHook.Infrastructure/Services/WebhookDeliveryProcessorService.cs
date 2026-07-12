using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Diagnostics;
using System.Text;
using WebHook.Core.Entities;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Utilities;

namespace WebHook.Infrastructure.Services;

public sealed class WebhookDeliveryProcessorService
{
    private readonly RepositoryContext _repositoryContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WebhookDeliveryRetryAfterService _retryAfterService;

    public WebhookDeliveryProcessorService(RepositoryContext repositoryContext, IHttpClientFactory httpClientFactory, WebhookDeliveryRetryAfterService retryAfterService)
    {
        _repositoryContext = repositoryContext;
        _httpClientFactory = httpClientFactory;
        _retryAfterService = retryAfterService;
    }

    private ILogger _logger = Log.ForContext(_className, nameof(WebhookDeliveryProcessorService));
    private const string _methodName = "MethodName";
    private const string _className = "ClassName";

    public async Task ProcessPendingDeliveriesAsync(int totalToProcess = 10, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(ProcessPendingDeliveriesAsync));

        try
        {
            _logger.Information("Begin processing of pending deliveries.....");

            List<WebhookDelivery> deliveriesToProcess = await _repositoryContext.WebhookDeliveries.FromSqlRaw
            (
                @"SELECT * FROM ""WebhookDeliveries"" WHERE ""DeliveryStatus"" = {0} AND ""NextRetryAt"" IS NULL ORDER BY ""CreatedAt"" LIMIT {1} FOR UPDATE SKIP LOCKED", WebHook.Core.Constants.WebhookDeliveryStatus.Pending.ToString(), totalToProcess
            ).ToListAsync(ct);

            if (!deliveriesToProcess.Any())
            {
                _logger.Information("No pending webhook deliveries to process at this time.");
                return;
            }

            var httpClient = _httpClientFactory.CreateClient("WebhookDeliveryClient");

            foreach (WebhookDelivery delivery in deliveriesToProcess)
            {
                _logger.Information("Beging processing of webhook delivery for - {0}", delivery.Id);

                string urlToCall = delivery.CallBackUrl;
                string payload = delivery.RequestPayload;

                var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                string responseContent = string.Empty;string httpResponseCode = string.Empty;
                //httpClient.BaseAddress = new Uri(urlToCall);
                //httpClient.Timeout = TimeSpan.FromSeconds(30);
                
                DateTimeOffset attemptedTime = DateTimeOffset.UtcNow;
                var stopWatch = new Stopwatch();
                stopWatch.Start();
                try
                {
                    _logger.Information("Pushing webhook delivery to call back url - {0}", urlToCall);
                    using HttpResponseMessage httpResponse = await httpClient.PostAsync(urlToCall, new StringContent(payload, encoding: Encoding.UTF8, "application/json"), requestCts.Token);
                    stopWatch.Stop();

                    responseContent = await httpResponse.Content.ReadAsStringAsync(requestCts.Token);
                    httpResponseCode = ((int)httpResponse.StatusCode).ToString();

                    if (httpResponse.IsSuccessStatusCode)
                    {
                        delivery.DeliveryStatus = Core.Constants.WebhookDeliveryStatus.Delivered;
                        delivery.DeliveredAt = attemptedTime;
                        delivery.RetryCount++;
                    }
                    else
                    {
                        delivery.DeliveryStatus = Core.Constants.WebhookDeliveryStatus.Failed;
                        delivery.RetryCount++;
                        delivery.NextRetryAt = await _retryAfterService.GetRetryAfter(attemptedTime, delivery.RetryCount + 1);
                    }
                }
                catch (HttpRequestException ex)
                {
                    stopWatch.Stop();
                    _logger.Error(ex, "An error occurred while calling the endpoint: {0}", urlToCall);
                    delivery.DeliveryStatus = Core.Constants.WebhookDeliveryStatus.Failed;
                    delivery.NextRetryAt = await _retryAfterService.GetRetryAfter(attemptedTime, delivery.RetryCount + 1);

                }

                delivery.WebhookDeliveryAttempts.Add
                (
                    new WebhookDeliveryAttempt()
                    {
                        AttemptedAt = attemptedTime,
                        Duration = stopWatch.Elapsed.Milliseconds,
                        HttpResponse = responseContent,
                        AttemptedCount = 1,
                        HttpResponseCode = httpResponseCode

                    }
                );
                _logger.Information("Saving webhook delivery attempt and webhook delivery changes to the database....");
                await _repositoryContext.SaveChangesAsync(requestCts.Token);
            }



        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while processing webhook deliveries.");
            return;
        }
    }
}
