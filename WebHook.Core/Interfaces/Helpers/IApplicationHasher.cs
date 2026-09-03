namespace WebHook.Core.Interfaces.Helpers;

public interface IApplicationHasher
{
    Task<string> HashSecret(string secret);
    Task<bool> ValidateHashedSecret(string hashedSecret, string secret);
}
