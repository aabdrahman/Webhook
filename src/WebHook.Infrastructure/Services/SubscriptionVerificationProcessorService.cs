using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Diagnostics;
using System.Text.Json;
using WebHook.Core.Constants;
using WebHook.Core.DataTransferObjects.SubscriptionVerification;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

public class SubscriptionVerificationProcessorService
{
    private readonly RepositoryContext _repositoryContext;
    private readonly ISignatureService _signatureService;
    private readonly IHttpClientFactory _httpClientFactory;

    public SubscriptionVerificationProcessorService(RepositoryContext repositoryContext, ISignatureService signatureService, IHttpClientFactory httpClientFactory)
    {
        _repositoryContext = repositoryContext;
        _signatureService = signatureService;
        _httpClientFactory = httpClientFactory;
    }

    private ILogger _logger = Log.ForContext("ClassName", nameof(SubscriptionVerificationProcessorService));
    private const string _methodName = "MethodName";

    public async Task ProcessSignatureVerificationAsync(Guid verificationId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(ProcessSignatureVerificationAsync));
        try
        {
            _logger.Information("Begin processing signature verification for verification id - {0}", verificationId);
            WebhookSubscriptionVerification? verificationRecord = await _repositoryContext.WebhookSubscriptionVerifications
                                        .Include(x => x.Subscription)
                                        .FirstOrDefaultAsync(x => x.Id == verificationId && x.Status == WebhookSubscriptionVerificationStatus.Pending && x.Subscription.OnboardStatus == SubscriptionOnboardStatus.Inactive, 
                                                        cancellationToken: ct);
            
            if (verificationRecord is null)
            {
                _logger.Error("Signature verification record with id: {0} not found.", verificationId);
                return;
            }

            // Call the signature service to generate the signature for the subscription with the subscription secret key.
            string generatedSignature = _signatureService.GenerateSignature(payLoad: verificationRecord.ChallengeToken, encryptionKey: verificationRecord.Subscription.SecretKey);

            //Create a new HttpClient instance using the factory
            var httpClient = _httpClientFactory.CreateClient("WebhookSubscriptionVerificationClient");
            httpClient.BaseAddress = new Uri(verificationRecord.Subscription.CallbackUrl);

            //Add Headers for signature verification
            httpClient.DefaultRequestHeaders.Add("X-Webhook-Challenge-Signature", generatedSignature);

            // Prepare the request to the callback URL with the generated signature, this will be a POST request with the challenge token in the body and to teh path /challenge.
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "challenge")
            {
                Content = new StringContent(verificationRecord.ChallengeToken)
            };

            // Send the request and get the response
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            DateTimeOffset attemptedAt = DateTimeOffset.UtcNow;
            try
            {
                var response = await httpClient.SendAsync(httpRequest, ct);
                stopWatch.Stop();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.Error("Signature verification failed for verification id - {0}. Callback URL responded with status code: {1} after {2} ms", verificationId, response.StatusCode, stopWatch.ElapsedMilliseconds);
                    
                    verificationRecord.ResponseStatusCode = ((int)response.StatusCode).ToString();
                    verificationRecord.ResponseDetails = await response.Content.ReadAsStringAsync(ct);
                    verificationRecord.Status = WebhookSubscriptionVerificationStatus.Failed;
                }
                else
                {
                    _logger.Information("Signature verification request sent successfully for verification id - {0}. Callback URL responded with status code: {1} after {2} ms", verificationId, response.StatusCode, stopWatch.ElapsedMilliseconds);

                    string responseContent = await response.Content.ReadAsStringAsync(ct);
                    var deserialiedResponse = JsonSerializer.Deserialize<SignatureVerificationResponseDto>(responseContent);

                    if(deserialiedResponse is not null)
                    {
                        var isValidSignature = _signatureService.VerifySignature(payLoad: verificationRecord.ChallengeToken, receivedSignature: deserialiedResponse.Challenge, encryptionKey: verificationRecord.Subscription.SecretKey);
                        verificationRecord.Subscription.OnboardStatus = isValidSignature ? SubscriptionOnboardStatus.Active : SubscriptionOnboardStatus.Inactive;
                        verificationRecord.Subscription.IsActive = isValidSignature ? true : verificationRecord.Subscription.IsActive;
                        verificationRecord.Status = isValidSignature ? WebhookSubscriptionVerificationStatus.Success : WebhookSubscriptionVerificationStatus.Failed;
                        verificationRecord.ValidatedAt = isValidSignature ? DateTimeOffset.UtcNow : null;
                    }
                    else
                    {
                        _logger.Information("Signature verification response deserialization failed for verification id - {0}. Response content: {1}", verificationId, responseContent);
                        verificationRecord.Status = WebhookSubscriptionVerificationStatus.Failed;
                        
                    }
                    

                    verificationRecord.ResponseStatusCode = ((int)response.StatusCode).ToString();
                    verificationRecord.ResponseDetails = await response.Content.ReadAsStringAsync(ct);
                    
                }
            }
            catch (Exception ex)
            {
                stopWatch.Stop();
                _logger.Error(ex, "An error occurred while sending verification request for verification id - {0} after {1} ms", verificationId, stopWatch.ElapsedMilliseconds);

                verificationRecord.Status = WebhookSubscriptionVerificationStatus.Failed;
                verificationRecord.ResponseStatusCode = "500";
                verificationRecord.ResponseDetails = "An error occurred while sending the verification request.";
            }
            //Set the attempted at timestamp for the verification record
            verificationRecord.AttemptedAt = attemptedAt;

            //Update the verification record in the database with the response details and status

            await _repositoryContext.SaveChangesAsync(ct);

            _logger.Information("Completed processing signature verification for verification id - {0}. Result: {1}", verificationId, verificationRecord.Status.ToString());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while processing signature verification for verification id - {0}", verificationId);
        }
    }

}
