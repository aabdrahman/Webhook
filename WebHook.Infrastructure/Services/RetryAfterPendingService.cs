using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using WebHook.Core.Constants;
using WebHook.Core.Entities;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Utilities;

namespace WebHook.Infrastructure.Services;

public sealed class RetryAfterPendingService
{
    private readonly RepositoryContext _repositoryContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WebhookDeliveryRetryAfterService _retryAfterService;

    public RetryAfterPendingService(RepositoryContext repositoryContext, IHttpClientFactory httpClientFactory, WebhookDeliveryRetryAfterService retryAfterService)
    {
        _repositoryContext = repositoryContext;
        _logger = Log.ForContext("ClassName", nameof(RetryAfterPendingService));
        _httpClientFactory = httpClientFactory;
        _retryAfterService = retryAfterService;
    }

    private ILogger _logger;

    public async Task RunRetryAfterFirstAttemptAsync(int totalAttempts = 10, int maximumAttemptCount = 5, long thresholdDuration = 25000, CancellationToken ct = default)
    {
        _logger = _logger.ForContext("MethodName", nameof(RunRetryAfterFirstAttemptAsync));

        try
        {
            _logger.Information("Begin processing deliveries to retry......");

            var deliveriesToReattmpt = await _repositoryContext.WebhookDeliveries
                                                    .FromSqlRaw(@"SELECT * FROM ""WebhookDeliveries"" WHERE ""RetryCount"" >= 1 AND ""DeliveryStatus"" = {0} AND ""NextRetryAt"" <= CURRENT_TIMESTAMP ORDER BY ""CreatedAt"" LIMIT {1} FOR UPDATE SKIP LOCKED", WebhookDeliveryStatus.Failed.ToString(), totalAttempts)
                                                    .ToListAsync(ct);

            //Instantiate the client factory
            var httpClient = _httpClientFactory.CreateClient("WebhookDeliveryClient");

            foreach (WebhookDelivery delivery in deliveriesToReattmpt)
            {
                DateTimeOffset attemptedTime = DateTimeOffset.UtcNow;
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                try
                {
                    _logger.Information("Pushing the reqeuest to the callback url - {0} - {1}", delivery.CallBackUrl, delivery.RequestPayload);
                    using var httpResponse = await httpClient.PostAsJsonAsync(delivery.CallBackUrl, new StringContent(delivery.RequestPayload, encoding: Encoding.UTF8, "application/Json"));
                    stopwatch.Stop();
                    var responseContent = await httpResponse.Content.ReadAsStringAsync();
                    _logger.Information("The callback reponds after - {0}ms", stopwatch.Elapsed.TotalMilliseconds);

                    if (httpResponse.IsSuccessStatusCode)
                    {
                        delivery.DeliveryStatus = WebhookDeliveryStatus.Delivered;
                        delivery.RetryCount++;
                        delivery.NextRetryAt = await _retryAfterService.GetRetryAfter(attemptedTime, delivery.RetryCount + 1);
                        delivery.DeliveredAt = attemptedTime;
                        delivery.WebhookDeliveryAttempts.Add(new WebhookDeliveryAttempt()
                        {
                            WebhookDeliveryId = delivery.Id,
                            AttemptedAt = DateTimeOffset.UtcNow,
                            AttemptedCount = 1,
                            HttpResponse = responseContent,
                            HttpResponseCode = httpResponse.StatusCode.ToString(),
                            Duration = stopwatch.Elapsed.Milliseconds
                        });
                    }
                    else
                    {
                        delivery.DeliveryStatus = WebhookDeliveryStatus.Failed;
                        delivery.RetryCount++;
                        delivery.NextRetryAt = await _retryAfterService.GetRetryAfter(attemptedTime, delivery.RetryCount + 1);

                        delivery.WebhookDeliveryAttempts.Add(new WebhookDeliveryAttempt()
                        {
                            WebhookDeliveryId = delivery.Id,
                            AttemptedAt = DateTimeOffset.UtcNow,
                            AttemptedCount = 1,
                            HttpResponse = responseContent,
                            HttpResponseCode = httpResponse.StatusCode.ToString(),
                            Duration = stopwatch.Elapsed.Milliseconds
                        });
                       
                    }

                }
                catch (HttpRequestException ex)
                {
                    _logger.Error(ex, "An erroor ocurred while pushing the webhook.....");
                    continue;
                }

                // Check if the value has exceeded the point to push to dead letter.
                // This can be optimized further to escalate to a reci[pinet that the callback url is not accessible
                //Also, a possibility of the escalation of long responding endpoints to be done after already saving the details.
                if (delivery.RetryCount > maximumAttemptCount)
                {
                    delivery.DeliveryStatus = WebhookDeliveryStatus.DeadLetter;
                    delivery.webhookDeadLetterQueues.Add(new WebhookDeadLetterQueue()
                    {
                        Reason = $"Delivery attempted count exceeded threshold value: {maximumAttemptCount}",
                        WebhookDeliveryId = delivery.Id,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }

                await _repositoryContext.SaveChangesAsync(ct);

                if(stopwatch.Elapsed.Milliseconds > thresholdDuration)
                {

                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while processing deliveries to retry.....");
            return;
        }
    }
}
