using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Diagnostics;
using System.Text;
using WebHook.Core.Constants;
using WebHook.Core.DataTransferObjects.WebhookDelivery;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Utilities;

namespace WebHook.Infrastructure.Services;

public sealed class RetryAfterPendingService
{
    private readonly RepositoryContext _repositoryContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WebhookDeliveryRetryAfterService _retryAfterService;
    private readonly IEncryptionService _encryptionService;
    private readonly ISignatureService _signatureService;
    private readonly EmailContentFormatterHelper _emailContentFormatterHelper;

    public RetryAfterPendingService(RepositoryContext repositoryContext, IHttpClientFactory httpClientFactory, 
                                    WebhookDeliveryRetryAfterService retryAfterService, IEncryptionService encryptionService, 
                                    ISignatureService signatureService, EmailContentFormatterHelper emailContentFormatterHelper)
    {
        _repositoryContext = repositoryContext;
        _httpClientFactory = httpClientFactory;
        _retryAfterService = retryAfterService;
        _encryptionService = encryptionService;
        _signatureService = signatureService;
        _emailContentFormatterHelper = emailContentFormatterHelper;
        _logger = Log.ForContext("ClassName", nameof(RetryAfterPendingService));
    }

    private ILogger _logger;

    public async Task RunRetryAfterFirstAttemptAsync(int totalAttempts = 10, int maximumAttemptCount = 5, long thresholdDuration = 25000, string workerId = "", double lockDuration = 15, CancellationToken ct = default)
    {
        _logger = _logger.ForContext("MethodName", nameof(RunRetryAfterFirstAttemptAsync));

        try
        {
            _logger.Information("Begin processing deliveries to retry with parameters: BatchSize - {0}, maximumAttemptedCount - {1}, ThresholdDuration - {2}, WorkerId - {3}, LockDuration - {4}......", totalAttempts, maximumAttemptCount, thresholdDuration, workerId, lockDuration);

            //Create the transaction for the select for update
            await using var transaction = await _repositoryContext.Database.BeginTransactionAsync(ct);

            //Run the select for update script to pick all pending requests
            var deliveriesToReattmpt = await _repositoryContext.WebhookDeliveries
                                                    .FromSqlRaw(@"SELECT * 
                                                                  FROM ""WebhookDeliveries""
                                                                  WHERE ""RetryCount"" >= 1 AND ""DeliveryStatus"" = {0} AND ""NextRetryAt"" <= CURRENT_TIMESTAMP AND (""LockedUntil"" IS NULL OR ""LockedUntil"" < CURRENT_TIMESTAMP) 
                                                                  ORDER BY ""CreatedAt"" 
                                                                  LIMIT {1} 
                                                                  FOR UPDATE SKIP LOCKED", WebhookDeliveryStatus.Failed.ToString(), totalAttempts)
                                                    .ToListAsync(ct);

            if (!deliveriesToReattmpt.Any())
            {
                _logger.Information("No delivery to process currently.");
                return; 
            }

            //Loop through and set the status to processing and also the lockeduntil and lockedBy
            foreach (WebhookDelivery deliveryAttempt in deliveriesToReattmpt)
            {
                deliveryAttempt.DeliveryStatus = WebhookDeliveryStatus.Processing;
                deliveryAttempt.LockedUntil = DateTimeOffset.UtcNow.AddSeconds(lockDuration);
                deliveryAttempt.LockedBy = workerId;
            }

            try
            {
                //Commit and save the changes to database
                await _repositoryContext.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "An error occurred while saving deliveries as processing.");  
                await transaction.RollbackAsync(ct);
                return;
            }

            //After successful locking of the selected deliveries, we now get the metadata necessary for sending webhook to callback url for the deliveries.
            var deliveryIds = deliveriesToReattmpt.Select(x => x.Id).ToList();

            //Get teh metadata hosted in the otehr tables from database.
            var deliveriesMetadata = await _repositoryContext.WebhookDeliveries      
                                                        .Where(x => deliveryIds.Contains(x.Id))
                                                        .Select(x => new WebhookDeliveryProcessorMetadataDto() 
                                                                        { 
                                                                            DeliveryId = x.Id, 
                                                                            EncryptedSecret = x.WebhookSubscriptionEvent.webhookSubscription.SecretKey, 
                                                                            RaisedEventName = x.webhookEvent.EventType.ToUpper(), 
                                                                            ContactEmail = x.WebhookSubscriptionEvent.webhookSubscription.ContactEmail ?? "",
                                                                            ContactName = x.WebhookSubscriptionEvent.webhookSubscription.Name,
                                                                            SubscriptionName = x.WebhookSubscriptionEvent.webhookSubscription.Name,
                                                                            AverageResponseTime = x.WebhookDeliveryAttempts.Average(x => x.Duration),
                                                                            FirstAttemptedAt = x.WebhookDeliveryAttempts.FirstOrDefault().AttemptedAt,
                                                                            FirstAttemptedResponseCode = x.WebhookDeliveryAttempts.First().HttpResponseCode,
                                                                            EventId = x.WebhookSubscriptionEvent.Id
                                                                        })
                                                        .ToDictionaryAsync(x => x.DeliveryId, ct);

            //Validate the delivery metadata to ensure all necessary itesms are fetched.
            if(deliveriesMetadata.Count != deliveryIds.Count)
            {
                _logger.Error("An error occurred while fetching metadata. The count of the delivery ids - {0} is not the same as that of the fetced metadata - {1}", deliveryIds.Count, deliveriesMetadata.Count);
                return; //This is returned and lock not released because there is a seperate worker that releases locked deliveries.
            }


            //Instantiate the client factory.
            var httpClient = _httpClientFactory.CreateClient("WebhookDeliveryClient");

            //Begin looping for fetched deliveries
            foreach (WebhookDelivery delivery in deliveriesToReattmpt)
            {
                DateTimeOffset attemptedTime = DateTimeOffset.UtcNow;

                if(!deliveriesMetadata.TryGetValue(delivery.Id, out var deliveryMetadata))
                {
                    _logger.Error("Delivery metadata does not exists for delivery - {0}", delivery.Id);
                    continue; //This continues to other delivery in the loop.
                }

                var stopwatch = new Stopwatch();
                string httpResponseStatusCode = string.Empty;
                stopwatch.Start();
                using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct); //A scoped per-delivery cancellation token created from teh main one. this ensures that each delivery has its own cancellation 
                try
                {
                    _logger.Information("Pushing the request to the callback url - {0} - {1}", delivery.CallBackUrl, delivery.RequestPayload);

                    //Create the http request item for the delivery to send to callback url.
                    var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(delivery.CallBackUrl))
                    {
                        Content = new StringContent(delivery.RequestPayload, encoding: Encoding.UTF8, "application/Json")
                    };

                    httpRequest.Headers.Add("X-Webhook-Timestamp", attemptedTime.ToUnixTimeSeconds().ToString());
                    httpRequest.Headers.Add("X-Webhook-Event", deliveryMetadata.RaisedEventName);
                    httpRequest.Headers.Add("-Webhook-Signature", _signatureService.GenerateSignature(delivery.RequestPayload, _encryptionService.Decrypt(deliveryMetadata.EncryptedSecret)));

                    //using var content = new StringContent(delivery.RequestPayload, encoding: Encoding.UTF8, "application/Json");
                    //using var httpResponse = await httpClient.PostAsync(delivery.CallBackUrl, content, cancellationToken: requestCts.Token);
                    using var httpResponse = await httpClient.SendAsync(httpRequest, requestCts.Token);
                    stopwatch.Stop();
                    var httpDuration = stopwatch.Elapsed.TotalMilliseconds;
                    var responseContent = await httpResponse.Content.ReadAsStringAsync();
                    httpResponseStatusCode = httpResponse.StatusCode.ToString();
                    _logger.Information("The callback reponds after - {0}ms", stopwatch.Elapsed.TotalMilliseconds);

                    if (httpResponse.IsSuccessStatusCode)
                    {
                        delivery.DeliveryStatus = WebhookDeliveryStatus.Delivered;
                        delivery.RetryCount++;
                        delivery.DeliveredAt = attemptedTime;
                        delivery.LockedBy = null;
                        delivery.LockedUntil = null;
                        delivery.WebhookDeliveryAttempts.Add(new WebhookDeliveryAttempt()
                        {
                            WebhookDeliveryId = delivery.Id,
                            AttemptedAt = DateTimeOffset.UtcNow,
                            AttemptedCount = 1,
                            HttpResponse = responseContent,
                            HttpResponseCode = httpResponse.StatusCode.ToString(),
                            Duration = httpDuration
                        });
                    }
                    else
                    {
                        delivery.DeliveryStatus = WebhookDeliveryStatus.Failed;
                        delivery.RetryCount++;
                        delivery.NextRetryAt = await _retryAfterService.GetRetryAfter(attemptedTime, delivery.RetryCount + 1);
                        delivery.LockedBy = null;
                        delivery.LockedUntil = null;

                        delivery.WebhookDeliveryAttempts.Add(new WebhookDeliveryAttempt()
                        {
                            WebhookDeliveryId = delivery.Id,
                            AttemptedAt = DateTimeOffset.UtcNow,
                            AttemptedCount = 1,
                            HttpResponse = responseContent,
                            HttpResponseCode = httpResponse.StatusCode.ToString(),
                            Duration = httpDuration
                        });
                       
                    }

                }
                catch (HttpRequestException ex)
                {
                    _logger.Error(ex, "An erroor ocurred while pushing the webhook.....");
                    httpResponseStatusCode = ex.StatusCode.HasValue ? ex.StatusCode.Value.ToString() : "500";
                    delivery.DeliveryStatus = WebhookDeliveryStatus.Failed;
                    delivery.RetryCount++;
                    delivery.NextRetryAt = await _retryAfterService.GetRetryAfter(attemptedTime, delivery.RetryCount + 1);
                    delivery.LockedBy = null;
                    delivery.LockedUntil = null;

                    delivery.WebhookDeliveryAttempts.Add(new WebhookDeliveryAttempt()
                    {
                        WebhookDeliveryId = delivery.Id,
                        AttemptedAt = DateTimeOffset.UtcNow,
                        AttemptedCount = 1,
                        HttpResponse = ex.Message,
                        HttpResponseCode = ex.StatusCode.HasValue ? ex.StatusCode.Value.ToString() : "500",
                        Duration = stopwatch.Elapsed.TotalMilliseconds
                    });
                }

                // Check if the value has exceeded the point to push to dead letter.
                // This can be optimized further to escalate to a recipient that the callback url is not accessible
                // Also, a possibility of the escalation of long responding endpoints to be done after already saving the details.
                if (delivery.RetryCount > maximumAttemptCount && delivery.DeliveryStatus == WebhookDeliveryStatus.Failed)
                {
                    delivery.DeliveryStatus = WebhookDeliveryStatus.DeadLetter;
                    delivery.webhookDeadLetterQueues.Add(new WebhookDeadLetterQueue()
                    {
                        Reason = $"Delivery attempted count exceeded threshold value: {maximumAttemptCount}",
                        WebhookDeliveryId = delivery.Id,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }

                //Begin databse operation for the sent delivery.
                try
                {
                    await _repositoryContext.SaveChangesAsync(requestCts.Token);

                    if (delivery.DeliveryStatus == WebhookDeliveryStatus.Delivered)
                        _logger.Information("Webhook delivered successfully - {0}", delivery.Id);
                    else
                        _logger.Warning("Webhook could not be delivered for delivery - {0}", delivery.Id);

                    DateTimeOffset notificationTimestamp = DateTimeOffset.UtcNow;
                    //Escalation for the deadletter queue.
                    if(delivery.DeliveryStatus == WebhookDeliveryStatus.DeadLetter)
                    {
                        _logger.Information("Getting mail notification content for deadletter queue for delivery - {0}", delivery.Id);

                        if (string.IsNullOrEmpty(deliveryMetadata.ContactEmail))
                        {
                            _logger.Warning("No contact email is profiled for the subscription - {0} to escaltae delivery dead letter - {1}", deliveryMetadata.SubscriptionName, deliveryMetadata.DeliveryId);
                            continue;
                        }

                        Dictionary<string, string> mailContentParametrs = new Dictionary<string, string>()
                        {
                            { "ContactName", deliveryMetadata.ContactName },
                            { "ContactEmail",deliveryMetadata.ContactEmail },
                            { "SubscriptionName", deliveryMetadata.SubscriptionName },
                            { "CallbackUrl", delivery.CallBackUrl },
                            { "DeliveryId", delivery.Id.ToString() },
                            { "RetryCount", delivery.RetryCount.ToString() },
                            { "EventId", deliveryMetadata.EventId.ToString() },
                            { "EventType", deliveryMetadata.RaisedEventName },
                            { "MaxRetryCount" , maximumAttemptCount.ToString()},
                            { "FirstAttemptedAt", deliveryMetadata.FirstAttemptedAt.Value.ToString("R") },
                            { "LastAttemptedAt", attemptedTime.ToString("R") },
                            { "NotificationTimestamp", notificationTimestamp.ToString("R") },
                            { "DeliveryHistoryUrl", "" },
                            { "SubscriptionManagementUrl", "" },
                            { "FirstFailureCode", deliveryMetadata?.FirstAttemptedResponseCode?.ToString() ?? "" }
                        };

                        var mailContentToSend = await _emailContentFormatterHelper.GetEmailContentAsync(NotificationType.DeadLetterNotification, mailContentParametrs);

                        if (string.IsNullOrWhiteSpace(mailContentToSend))
                        {
                            _logger.Warning("Mail content for the escalation could not be fetched for delivery - {0}. Mail send abolished....", delivery.Id);
                            continue;
                        }
                    }

                    //Escalation when the HTTP call exceeds the threshold duration.
                    if (stopwatch.Elapsed.TotalMilliseconds > thresholdDuration && delivery.DeliveryStatus != WebhookDeliveryStatus.DeadLetter)
                    {
                        if (string.IsNullOrEmpty(deliveryMetadata.ContactEmail))
                        {
                            _logger.Warning("No contact email is profiled for the 4subscription - {0} to escaltae delivery - {1}", deliveryMetadata.SubscriptionName, deliveryMetadata.DeliveryId);
                            continue;
                        }

                        Dictionary<string, string> mailContentParameters = new Dictionary<string, string>()
                        {
                            { "ContactName", deliveryMetadata.ContactName },
                            { "ContactEmail",deliveryMetadata.ContactEmail },
                            { "SubscriptionName", deliveryMetadata.SubscriptionName },
                            { "CallbackUrl", delivery.CallBackUrl },
                            { "DeliveryId", delivery.Id.ToString() },
                            { "EventType", deliveryMetadata.RaisedEventName },
                            { "ResponseTimeMs", stopwatch.Elapsed.TotalMilliseconds.ToString("N2") },
                            { "AverageResponseTime", deliveryMetadata.AverageResponseTime.Value.ToString("N2") },
                            { "ThresholdMs", thresholdDuration.ToString("N2") },
                            { "HttpStatusCode", httpResponseStatusCode },
                            { "DetectedAt", attemptedTime.ToString("R") },
                            { "NotificationTimestamp", notificationTimestamp.ToString("R") },
                            { "DeliveryHistoryUrl", "" },
                            { "SubscriptionManagementUrl", "" }
                        };

                        string? mailContentToSend = await _emailContentFormatterHelper.GetEmailContentAsync(NotificationType.SlowEndpointNotification, mailContentParameters);

                        if (string.IsNullOrWhiteSpace(mailContentToSend))
                        {
                            _logger.Warning("Mail content for the escalation could not be fetched for delivery - {0}. Mail send abolished....", delivery.Id);
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    //The state is set to detahed to ensure that the chaneg tracker is not polluted for failed items.
                    foreach (var deliveryAttempt in delivery.WebhookDeliveryAttempts)
                        _repositoryContext.Entry(deliveryAttempt).State = EntityState.Detached;

                    foreach(var deliveryDeadLetter in delivery.webhookDeadLetterQueues)
                        _repositoryContext.Entry(deliveryDeadLetter).State = EntityState.Detached;

                    _repositoryContext.Entry(delivery).State = EntityState.Detached;
                    _logger.Information(ex, "An erroro occurred while saving delivery details for - {0}", delivery.Id);
                    continue;
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
