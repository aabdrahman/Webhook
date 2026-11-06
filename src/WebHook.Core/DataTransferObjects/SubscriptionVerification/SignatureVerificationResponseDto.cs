using System.Text.Json.Serialization;

namespace WebHook.Core.DataTransferObjects.SubscriptionVerification;

/// <summary>
/// The body a subscriber returns from its <c>/challenge</c> endpoint: <c>{"response":"sha256=&lt;hex&gt;"}</c>.
/// </summary>
public class SignatureVerificationResponseDto
{
    /// <summary>
    /// <c>sha256=</c> followed by the hex HMAC-SHA256, keyed with the subscription secret, of
    /// <c>"challenge-response:"</c> plus the challenge token from the request.
    /// </summary>
    public string Response { get; set; }

    /// <summary>Initialises a new instance of the <see cref="SignatureVerificationResponseDto"/> class.</summary>
    /// <param name="Response">The signed challenge response.</param>
    [JsonConstructor]
    public SignatureVerificationResponseDto(string Response)
    {
        this.Response = Response;
    }
}
