using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Diagnostics;
using System.Text;
using WebHook.Core.Constants;
using WebHook.Core.DataTransferObjects.EmailSender;
using WebHook.Core.DataTransferObjects.WebhookDelivery;
using WebHook.Core.Entities;
using WebHook.Core.EventContracts.Publishers;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Utilities;

namespace WebHook.Infrastructure.Services;

/// <summary>
/// Makes the first HTTP attempt for pending deliveries. Used by the delivery processor worker.
/// </summary>
/// <remarks>
/// <para>
/// Delivery uses a two-phase claim. First, a batch of <see cref="WebhookDeliveryStatus.Pending"/>
/// deliveries with no retry scheduled and no live lock is selected with
/// <c>FOR UPDATE SKIP LOCKED</c>, marked <see cref="WebhookDeliveryStatus.Processing"/> and locked
/// to the worker, and the transaction is committed. Then each delivery is posted to its callback URL
/// outside the transaction.
/// </para>
/// <para>
/// Each request is sent with the <c>X-Webhook-Timestamp</c> (Unix seconds), <c>X-Webhook-Event</c>
/// and <c>X-Webhook-Signature</c> headers; the signature covers <c>"{timestamp}.{payload}"</c>. A 2xx response marks the delivery
/// <see cref="WebhookDeliveryStatus.Delivered"/>; anything else, an <see cref="HttpRequestException"/>,
/// or a request timeout (recorded as <c>408</c>), marks it <see cref="WebhookDeliveryStatus.Failed"/> with a
/// <c>NextRetryAt</c> from <see cref="WebhookDeliveryRetryAfterService"/>. Every attempt is recorded.
/// If the request takes longer than the threshold, a slow-endpoint email is queued to the
/// subscription's contact.
/// </para>
/// <para>
/// Any other exception, including cancellation on shutdown, ends the whole batch; the remaining
/// claimed deliveries stay locked until <see cref="StaleClaimedDeliveryReleaseService"/> releases them.
/// </para>
/// </remarks>
public sealed class WebhookDeliveryProcessorService
{
    private readonly RepositoryContext _repositoryContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WebhookDeliveryRetryAfterService _retryAfterService;
    private readonly ISignatureService _signatureService;
    private readonly IEncryptionService _encryptionService;
    private readonly EmailContentFormatterHelper _emailContentFormatterHelper;
    private readonly IApplicationPublisher _applicationPublisher;

    /// <summary>
    /// Initialises a new instance of the <see cref="WebhookDeliveryProcessorService"/> class.
    /// </summary>
    /// <param name="repositoryContext">The database context.</param>
    /// <param name="httpClientFactory">Supplies the named <c>WebhookDeliveryClient</c> HTTP client.</param>
    /// <param name="retryAfterService">Computes when a failed delivery should be retried.</param>
    /// <param name="signatureService">Signs request payloads.</param>
    /// <param name="encryptionService">Decrypts subscription signing secrets.</param>
    /// <param name="emailContentFormatterHelper">Renders the slow-endpoint email.</param>
    /// <param name="applicationPublisher">Queues escalation emails.</param>
    public WebhookDeliveryProcessorService(RepositoryContext repositoryContext, IHttpClientFactory httpClientFactory, WebhookDeliveryRetryAfterService retryAfterService, ISignatureService signatureService, IEncryptionService encryptionService, EmailContentFormatterHelper emailContentFormatterHelper, IApplicationPublisher applicationPublisher)
    {
        _repositoryContext = repositoryContext;
        _httpClientFactory = httpClientFactory;
        _retryAfterService = retryAfterService;
        _signatureService = signatureService;
        _encryptionService = encryptionService;
        _emailContentFormatterHelper = emailContentFormatterHelper;
        _applicationPublisher = applicationPublisher;
    }

    private ILogger _logger = Log.ForContext(_className, nameof(WebhookDeliveryProcessorService));
    private const string _methodName = "MethodName";
    private const string _className = "ClassName";

    /// <summary>
    /// Claims a batch of pending deliveries and makes one HTTP attempt for each.
    /// </summary>
    /// <remarks>Errors are logged, not thrown.</remarks>
    /// <param name="totalToProcess">The maximum number of deliveries to claim.</param>
    /// <param name="lockDuration">How long, in seconds, each claimed delivery stays locked to this worker.</param>
    /// <param name="workerId">The ID recorded as the delivery's <c>LockedBy</c>.</param>
    /// <param name="thresholdDuration">The HTTP duration, in milliseconds, above which a slow-endpoint email is sent.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A task that completes when the batch has been processed.</returns>
    public async Task ProcessPendingDeliveriesAsync(int totalToProcess = 10, double lockDuration = 30, string workerId = "", long thresholdDuration = 1500, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(ProcessPendingDeliveriesAsync));

        try
        {
            _logger.Information("Begin processing of pending deliveries.....");

            //Create Database transaction for the select
            await using var transaction = await _repositoryContext.Database.BeginTransactionAsync(ct);

            //Begin to select the deliveries from database.
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
                await transaction.CommitAsync(ct);
                return;
            }

            //Loop through all the selected deliveries to set status as processing using the transaction for the update-select.
            foreach (var deliveryItem in deliveriesToProcess)
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

            //Get the header properties from the database for the fetched deliveries.
            var deliveryIds = deliveriesToProcess.Select(x => x.Id).ToList();

            Dictionary<Guid, WebhookDeliveryProcessorMetadataDto> deliveryMetadatas = await _repositoryContext.WebhookDeliveries
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
                                                    FirstAttemptedAt = x.WebhookDeliveryAttempts.FirstOrDefault()!.AttemptedAt,
                                                    FirstAttemptedResponseCode = x.WebhookDeliveryAttempts.First().HttpResponseCode,
                                                    EventId = x.WebhookSubscriptionEvent.Id
                                                })
                                                .ToDictionaryAsync(x => x.DeliveryId, ct);
            //.ToListAsync(ct);

            if (!deliveryMetadatas.Any())
            {
                _logger.Error("An error occurred when fetching metadata. Metadata item does not contain any value.");
                return; //This is returned as aanother seperate worker will release this lock and this section will not do any update further.
            }

            if (deliveryMetadatas.Count != deliveryIds.Count)
            {
                _logger.Error("An error occurred while fetching metadata. The count for the metadata - {0} is not the same as the count of the delivery ids fetched - {1}", deliveryMetadatas.Count, deliveryIds.Count);
                return; //This is returned as another seperate worker will release this lock and this section will not do any update further.
            }

            //Instantiate the named client for http delivery to callback url.
            var httpClient = _httpClientFactory.CreateClient("WebhookDeliveryClient");

            //Loop through the selected deliveries to perform http operation(sending http call to the callback url)
            foreach (WebhookDelivery delivery in deliveriesToProcess)
            {
                _logger.Information("Beging processing of webhook delivery for - {0}", delivery.Id);

                //WebhookDeliveryProcessorMetadataDto? deliveryMetadata = deliveryMetadatas.FirstOrDefault(x => x.DeliveryId == delivery.Id);

                if (!deliveryMetadatas.TryGetValue(delivery.Id, out var deliveryMetadata))
                {
                    _logger.Error("The delivery metadata for the delivery could not be fetched due to unforesseen issues for the delivery - {0}.", delivery.Id);
                    continue;
                }

                //Scoped variables that specifies to the current http call.
                string urlToCall = delivery.CallBackUrl;
                string payload = delivery.RequestPayload;

                //Local variable scoped to teh delivery for utitlity operations.
                using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                string responseContent = string.Empty;
                string httpResponseCode = string.Empty;

                DateTimeOffset attemptedTime = DateTimeOffset.UtcNow;

                //Build the request item to call HTTP
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(urlToCall))
                {
                    Content = new StringContent(payload, encoding: Encoding.UTF8, "application/json")
                };

                //The same timestamp is sent in the header and bound into the signature, so it cannot be replaced on replay.
                long signatureTimestamp = attemptedTime.ToUnixTimeSeconds();
                httpRequest.Headers.Add("X-Webhook-Timestamp", signatureTimestamp.ToString());
                httpRequest.Headers.Add("X-Webhook-Event", deliveryMetadata.RaisedEventName.ToUpper());
                httpRequest.Headers.Add("X-Webhook-Signature", _signatureService.GenerateSignature(payload, _encryptionService.Decrypt(deliveryMetadata.EncryptedSecret), signatureTimestamp));

                var stopWatch = new Stopwatch();
                stopWatch.Start();
                try
                {
                    _logger.Information("Pushing webhook delivery to call back url - {0}", urlToCall);
                    //using var content = new StringContent(payload, encoding: Encoding.UTF8, "application/json");
                    //using HttpResponseMessage httpResponse = await httpClient.PostAsync(urlToCall, content, requestCts.Token);
                    using var httpResponse = await httpClient.SendAsync(httpRequest, requestCts.Token);
                    stopWatch.Stop();
                    var httpDuration = stopWatch.Elapsed.TotalMilliseconds;
                    _logger.Information("The callback reponds after - {0}ms", httpDuration);
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
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    stopWatch.Stop();
                    _logger.Error(ex, "A timeout occurred while calling the endpoint: {0}", urlToCall);
                    delivery.DeliveryStatus = WebhookDeliveryStatus.Failed;
                    delivery.RetryCount++;
                    delivery.NextRetryAt = await _retryAfterService.GetRetryAfter(attemptedTime, delivery.RetryCount + 1);
                    delivery.LockedBy = null;
                    delivery.LockedUntil = null;
                    responseContent = ex.Message;
                    httpResponseCode = ((int)StatusCodes.Status408RequestTimeout).ToString();
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
                    httpResponseCode = ex.StatusCode.HasValue ? ((int)ex.StatusCode.Value).ToString() : "500";
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

                //Begin database operation to save the details of the delivery and delivery attempt.
                try
                {
                    _logger.Information("Saving webhook delivery attempt and webhook delivery changes to the database....");
                    await _repositoryContext.SaveChangesAsync(requestCts.Token);

                    //After save changes, begin the possible escalatio for a slow http call.
                    if (stopWatch.Elapsed.TotalMilliseconds > thresholdDuration && delivery.DeliveryStatus != WebhookDeliveryStatus.DeadLetter)
                    {
                        if (string.IsNullOrEmpty(deliveryMetadata.ContactEmail))
                        {
                            _logger.Warning("No contact email is profiled for the 4subscription - {0} to escalate delivery - {1}", deliveryMetadata.SubscriptionName, deliveryMetadata.DeliveryId);
                            continue;
                        }

                        DateTimeOffset notificationTimestamp = DateTimeOffset.UtcNow;
                        Dictionary<string, string> mailContentParameters = new Dictionary<string, string>()
                        {
                            { "ContactName", deliveryMetadata.ContactName },
                            { "ContactEmail",deliveryMetadata.ContactEmail },
                            { "SubscriptionName", deliveryMetadata.SubscriptionName },
                            { "CallbackUrl", delivery.CallBackUrl },
                            { "DeliveryId", delivery.Id.ToString() },
                            { "EventType", deliveryMetadata.RaisedEventName },
                            { "ResponseTimeMs", stopWatch.Elapsed.TotalMilliseconds.ToString("N2") },
                            { "AverageResponseTime", deliveryMetadata.AverageResponseTime.HasValue ? deliveryMetadata.AverageResponseTime.Value.ToString("N2") : (0.00).ToString("N2") },
                            { "ThresholdMs", thresholdDuration.ToString("N2") },
                            { "HttpStatusCode", httpResponseCode },
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

                        //Begin formatting the email to queue for processing. We use the application publisher as a sinle source for channel publishing.
                        await _applicationPublisher.QueueEmailEvent(new EmailSenderDto(MailContent: mailContentToSend, Subject: "Webhook Callback Response Time Exceeded", MailRecipients: [deliveryMetadata.ContactEmail]));
                        //await _emailSenderChannel.Writer.WriteAsync(new EmailSenderDto(MailContent: mailContentToSend, Subject: "Webhook Callback Response Time Exceeded", MailRecipients: [deliveryMetadata.ContactEmail]));
                    }

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
