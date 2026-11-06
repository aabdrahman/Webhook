using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WebHook.Core.Constants;
using WebHook.Core.DataTransferObjects.SubscriptionVerification;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

/// <summary>
/// Performs the callback URL ownership handshake for a queued subscription verification.
/// </summary>
/// <remarks>
/// <para>
/// The handshake posts the stored challenge token to <c>{callbackUrl}/challenge</c> as <c>{"challenge":"&lt;token&gt;"}</c>
/// with <c>X-Webhook-Timestamp</c> and <c>X-Webhook-Challenge-Signature</c> headers. The signature is
/// <c>sha256=HMAC(secret, "{timestamp}.{body}")</c>, the same format as delivery signatures, so the receiver can
/// authenticate the platform before answering.
/// </para>
/// <para>
/// The receiver proves it holds the subscription secret by replying <c>200 {"response":"sha256=&lt;hex&gt;"}</c>, where the
/// value is <c>HMAC(secret, "challenge-response:" + token)</c>. The prefix means the answer never appears in the
/// request, so a server that merely echoes what it receives cannot pass. A correct answer marks the verification
/// <see cref="WebhookSubscriptionVerificationStatus.Success"/> and the subscription <see cref="SubscriptionOnboardStatus.Active"/>.
/// </para>
/// <para>
/// Challenges older than <see cref="ChallengeValidity"/> are failed without being sent, and stored response bodies are
/// capped at <see cref="MaxResponseBytes"/>. Errors are logged, not thrown.
/// </para>
/// </remarks>
public class SubscriptionVerificationProcessorService
{
    /// <summary>How long after creation a challenge may still be sent and answered.</summary>
    public static readonly TimeSpan ChallengeValidity = TimeSpan.FromMinutes(10);

    /// <summary>The prefix signed together with the token to form the receiver's answer, separating it from delivery signatures.</summary>
    public const string ChallengeResponsePayloadPrefix = "challenge-response:";

    /// <summary>The maximum number of response bytes read and stored.</summary>
    public const int MaxResponseBytes = 16 * 1024;

    private const string ChallengePath = "/challenge";
    private const string TimestampHeader = "X-Webhook-Timestamp";
    private const string SignatureHeader = "X-Webhook-Challenge-Signature";

    private readonly RepositoryContext _repositoryContext;
    private readonly ISignatureService _signatureService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEncryptionService _encryptionService;

    /// <summary>
    /// Initialises a new instance of the <see cref="SubscriptionVerificationProcessorService"/> class.
    /// </summary>
    /// <param name="repositoryContext">The database context.</param>
    /// <param name="signatureService">Signs the request and checks the receiver's answer.</param>
    /// <param name="httpClientFactory">Supplies the named <c>WebhookSubscriptionVerificationClient</c>.</param>
    /// <param name="encryptionService">Decrypts the subscription secret.</param>
    public SubscriptionVerificationProcessorService(RepositoryContext repositoryContext, ISignatureService signatureService, IHttpClientFactory httpClientFactory, IEncryptionService encryptionService)
    {
        _repositoryContext = repositoryContext;
        _signatureService = signatureService;
        _httpClientFactory = httpClientFactory;
        _encryptionService = encryptionService;
    }

    private ILogger _logger = Log.ForContext("ClassName", nameof(SubscriptionVerificationProcessorService));
    private const string _methodName = "MethodName";

    /// <summary>
    /// Sends the challenge for a <see cref="WebhookSubscriptionVerificationStatus.Pending"/> verification of an
    /// <see cref="SubscriptionOnboardStatus.Inactive"/> subscription and records the outcome.
    /// </summary>
    /// <remarks>
    /// Does nothing if the verification does not exist, is no longer pending, or its subscription is already active.
    /// </remarks>
    /// <param name="verificationId">The ID of the verification record.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A task that completes when the outcome has been saved.</returns>
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

            //A challenge that has waited too long (for example across a restart) is failed rather than sent.
            if (DateTimeOffset.UtcNow - verificationRecord.CreatedAt > ChallengeValidity)
            {
                _logger.Warning("Signature verification {0} expired before it could be sent.", verificationId);
                verificationRecord.Status = WebhookSubscriptionVerificationStatus.Failed;
                verificationRecord.ResponseDetails = "The verification challenge expired before it could be sent.";
                await _repositoryContext.SaveChangesAsync(ct);
                return;
            }

            string subscriberSecret = _encryptionService.Decrypt(verificationRecord.Subscription.SecretKey);

            DateTimeOffset attemptedAt = DateTimeOffset.UtcNow;
            long timestamp = attemptedAt.ToUnixTimeSeconds();
            string requestBody = JsonSerializer.Serialize(new { challenge = verificationRecord.ChallengeToken });

            //The signature covers "{timestamp}.{body}" so the receiver can authenticate the platform and reject replays.
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildChallengeUri(verificationRecord.Subscription.CallbackUrl))
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Add(TimestampHeader, timestamp.ToString());
            httpRequest.Headers.Add(SignatureHeader, _signatureService.GenerateSignature(requestBody, subscriberSecret, timestamp));

            var httpClient = _httpClientFactory.CreateClient("WebhookSubscriptionVerificationClient");

            var stopWatch = Stopwatch.StartNew();
            try
            {
                using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
                string responseContent = await ReadBoundedAsync(response.Content, ct);
                stopWatch.Stop();

                verificationRecord.ResponseStatusCode = ((int)response.StatusCode).ToString();
                verificationRecord.ResponseDetails = responseContent;

                if (!response.IsSuccessStatusCode)
                {
                    _logger.Error("Signature verification failed for verification id - {0}. Callback URL responded with status code: {1} after {2} ms", verificationId, response.StatusCode, stopWatch.ElapsedMilliseconds);
                    verificationRecord.Status = WebhookSubscriptionVerificationStatus.Failed;
                }
                else
                {
                    _logger.Information("Callback URL responded to verification id - {0} with status code: {1} after {2} ms", verificationId, response.StatusCode, stopWatch.ElapsedMilliseconds);

                    bool isValidResponse = IsChallengeResponseValid(responseContent, verificationRecord.ChallengeToken, subscriberSecret);

                    verificationRecord.Status = isValidResponse ? WebhookSubscriptionVerificationStatus.Success : WebhookSubscriptionVerificationStatus.Failed;
                    verificationRecord.ValidatedAt = isValidResponse ? DateTimeOffset.UtcNow : null;

                    if (isValidResponse)
                    {
                        verificationRecord.Subscription.OnboardStatus = SubscriptionOnboardStatus.Active;
                        verificationRecord.Subscription.IsActive = true;
                    }
                    else
                    {
                        _logger.Warning("Callback URL returned an invalid challenge response for verification id - {0}", verificationId);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                //The host is stopping: leave the record pending instead of recording a false failure.
                throw;
            }
            catch (TaskCanceledException ex)
            {
                //HttpClient reports its own timeout as a cancellation that the caller did not request.
                stopWatch.Stop();
                _logger.Error(ex, "The callback URL did not respond in time for verification id - {0} after {1} ms", verificationId, stopWatch.ElapsedMilliseconds);

                verificationRecord.Status = WebhookSubscriptionVerificationStatus.Failed;
                verificationRecord.ResponseStatusCode = "408";
                verificationRecord.ResponseDetails = "The callback URL did not respond in time.";
            }
            catch (Exception ex)
            {
                stopWatch.Stop();
                _logger.Error(ex, "An error occurred while sending verification request for verification id - {0} after {1} ms", verificationId, stopWatch.ElapsedMilliseconds);

                verificationRecord.Status = WebhookSubscriptionVerificationStatus.Failed;
                verificationRecord.ResponseStatusCode = "500";
                verificationRecord.ResponseDetails = "An error occurred while sending the verification request.";
            }

            verificationRecord.AttemptedAt = attemptedAt;

            await _repositoryContext.SaveChangesAsync(ct);

            _logger.Information("Completed processing signature verification for verification id - {0}. Result: {1}", verificationId, verificationRecord.Status.ToString());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while processing signature verification for verification id - {0}", verificationId);
        }
    }

    /// <summary>
    /// Appends <c>/challenge</c> to the callback URL's path, keeping its query string, so the result does not
    /// depend on whether the stored URL ends with a slash.
    /// </summary>
    private static Uri BuildChallengeUri(string callbackUrl)
    {
        var builder = new UriBuilder(callbackUrl);
        builder.Path = builder.Path.TrimEnd('/') + ChallengePath;
        return builder.Uri;
    }

    /// <summary>
    /// Checks that the receiver's reply is JSON containing <c>response</c> equal to
    /// <c>HMAC(secret, "challenge-response:" + token)</c>. The comparison is constant-time.
    /// </summary>
    private bool IsChallengeResponseValid(string responseContent, string challengeToken, string subscriberSecret)
    {
        SignatureVerificationResponseDto? challengeResponse;
        try
        {
            challengeResponse = JsonSerializer.Deserialize<SignatureVerificationResponseDto>(responseContent, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(challengeResponse?.Response))
            return false;

        return _signatureService.VerifySignature(payLoad: ChallengeResponsePayloadPrefix + challengeToken, receivedSignature: challengeResponse.Response, encryptionKey: subscriberSecret);
    }

    /// <summary>Reads at most <see cref="MaxResponseBytes"/> bytes of the response so a hostile endpoint cannot exhaust memory or storage.</summary>
    private static async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        var buffer = new byte[MaxResponseBytes];
        int total = 0, read;

        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct)) > 0)
            total += read;

        return Encoding.UTF8.GetString(buffer, 0, total);
    }
}
