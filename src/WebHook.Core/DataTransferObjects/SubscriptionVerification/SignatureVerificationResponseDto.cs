using System.Text.Json.Serialization;

namespace WebHook.Core.DataTransferObjects.SubscriptionVerification;

public class SignatureVerificationResponseDto
{
    public string Challenge { get; set; }

    [JsonConstructor]
    public SignatureVerificationResponseDto(string Challenge)
    {
        this.Challenge = Challenge;
    }
}
