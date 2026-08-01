using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Diagnostics;
using System.Text;
using WebHook.Core.Constants;
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

    public async Task ProcessPendingDeliveriesAsync(int totalToProcess = 10, double lockDuration = 30, string workerId = "", CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(ProcessPendingDeliveriesAsync));

        try
        {
            _logger.Information("Begin processing of pending deliveries.....");

            //Create Database transaction for the select
            await using var transaction = await _repositoryContext.Database.BeginTransactionAsync(ct);

            //Begin tos elect the deliveries from database.
            List<WebhookDelivery> deliveriesToProcess = await _repositoryContext.WebhookDeliveries.FromSqlRaw
            (
                @"  SELECT * 
                    FROM ""WebhookDeliveries"" 
                    WHERE ""DeliveryStatus"" = {0} AND ""NextRetryAt"" IS NULL AND (""LockedUntil"" IS NULL OR ""LockedUntil"" < CURRENT_TIMESTAMP) 
                    ORDER BY ""CreatedAt"" 
                    LIMIT {1}
                    FOR UPDATE SKIP LOCKED", WebhookDeliveryStatus.Pending.ToString(), totalToProcess
            ).ToListAsync(ct);

            //Check if deliveries are empty(i.e no delivery to process)
            if (!deliveriesToProcess.Any())
            {
                _logger.Information("No pending webhook deliveries to process at this time.");
                return;
            }

            //Loop through all the selected deliveries to set status as processing using the transaction for the update-select.
            foreach(var deliveryItem in deliveriesToProcess)
            {
                deliveryItem.LockedBy = workerId;
                deliveryItem.LockedUntil = DateTimeOffset.UtcNow.AddSeconds(lockDuration);
                deliveryItem.DeliveryStatus = WebhookDeliveryStatus.Processing;
            }

            //Begin saving changes to the selected deliveries and rollback the changes if it fails with an exception.
            try
            {
                _logger.Information("Begin saving changes made to the selected deliveries...");
                await _repositoryContext.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "An error occurred while setting the selected deliveries status to processing. Begin rollback process....");
                await transaction.RollbackAsync(ct);
                return;
            }

            //Instantiate the named client for http delivery to callback url.
            var httpClient = _httpClientFactory.CreateClient("WebhookDeliveryClient");

            //Loop through the selected deliveries to perform http operation(sending http call to the callback url)
            foreach (WebhookDelivery delivery in deliveriesToProcess)
            {
                _logger.Information("Beging processing of webhook delivery for - {0}", delivery.Id);

                string urlToCall = delivery.CallBackUrl;
                string payload = delivery.RequestPayload;

                using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                string responseContent = string.Empty;
                string httpResponseCode = string.Empty;

                
                DateTimeOffset attemptedTime = DateTimeOffset.UtcNow;
                var stopWatch = new Stopwatch();
                stopWatch.Start();
                try
                {
                    _logger.Information("Pushing webhook delivery to call back url - {0}", urlToCall);
                    using var content = new StringContent(payload, encoding: Encoding.UTF8, "application/json");
                    using HttpResponseMessage httpResponse = await httpClient.PostAsync(urlToCall, content, requestCts.Token);
                    stopWatch.Stop();
                    var httpDuration = stopWatch.Elapsed.TotalMilliseconds;
                    responseContent = await httpResponse.Content.ReadAsStringAsync(requestCts.Token);
                    httpResponseCode = ((int)httpResponse.StatusCode).ToString();

                    if (httpResponse.IsSuccessStatusCode)
                    {
                        delivery.DeliveryStatus = WebhookDeliveryStatus.Delivered;
                        delivery.DeliveredAt = attemptedTime;
                        delivery.RetryCount++;
                        delivery.LockedBy = null;
                        delivery.LockedUntil = null;
                    }
                    else
                    {
                        delivery.DeliveryStatus = WebhookDeliveryStatus.Failed;
                        delivery.RetryCount++;
                        delivery.NextRetryAt = await _retryAfterService.GetRetryAfter(attemptedTime, delivery.RetryCount + 1);
                        delivery.LockedBy = null;
                        delivery.LockedUntil = null;
                    }
                }
                catch (HttpRequestException ex)
                {
                    stopWatch.Stop();
                    _logger.Error(ex, "An error occurred while calling the endpoint: {0}", urlToCall);
                    delivery.DeliveryStatus = WebhookDeliveryStatus.Failed;
                    delivery.RetryCount++;
                    delivery.NextRetryAt = await _retryAfterService.GetRetryAfter(attemptedTime, delivery.RetryCount + 1);
                    delivery.LockedBy = null;
                    delivery.LockedUntil = null;
                    responseContent = ex.Message;
                    httpResponseCode = ex.StatusCode.HasValue ? ex.StatusCode.Value.ToString() : "500";
                }

                delivery.WebhookDeliveryAttempts.Add
                (
                    new WebhookDeliveryAttempt()
                    {
                        AttemptedAt = attemptedTime,
                        Duration = stopWatch.Elapsed.TotalMilliseconds,
                        HttpResponse = responseContent,
                        AttemptedCount = 1,
                        HttpResponseCode = httpResponseCode

                    }
                );

                //Begin databse operation to save the details of the delivery and delivery attempt.
                try
                {
                    _logger.Information("Saving webhook delivery attempt and webhook delivery changes to the database....");
                    await _repositoryContext.SaveChangesAsync(requestCts.Token);

                    if (delivery.DeliveryStatus == WebhookDeliveryStatus.Delivered)
                        _logger.Information("Webhook delivered successfully - {0}", delivery.Id);
                    else
                        _logger.Warning("Webhook could not be delivered for delivery - {0}", delivery.Id);
                }


                catch (Exception ex)
                {
                    //We are setting the state of a failed save to detached to prevent polluting the built-in ef core change tracker.
                    _logger.Error(ex, "An error occurred while saving delivery attempt and delivery changes - {0}", delivery.Id);

                    foreach (var deliveryAttempt in delivery.WebhookDeliveryAttempts)
                        _repositoryContext.Entry(deliveryAttempt).State = EntityState.Detached;

                    foreach (var deliveryDeadLetter in delivery.webhookDeadLetterQueues)
                        _repositoryContext.Entry(deliveryDeadLetter).State = EntityState.Detached;
                    _repositoryContext.Entry(delivery).State = EntityState.Detached;

                    continue;
                }
            }

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while processing webhook deliveries.");
            return;
        }
    }
}
