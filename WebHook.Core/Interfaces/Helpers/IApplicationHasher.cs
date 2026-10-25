namespace WebHook.Core.Interfaces.Helpers;

/// <summary>
/// One-way salted hashing for secrets that must be verified but never recovered,
/// such as service client keys, OTPs and operation tokens.
/// </summary>
public interface IApplicationHasher
{
    /// <summary>
    /// Hashes a secret with a new random salt.
    /// </summary>
    /// <param name="secret">The plaintext secret.</param>
    /// <returns>The hash and salt in a self-contained string format, or an empty string if hashing failed.</returns>
    Task<string> HashSecret(string secret);
    /// <summary>
    /// Checks a plaintext secret against a hash produced by <see cref="HashSecret"/>, in constant time.
    /// </summary>
    /// <param name="hashedSecret">The stored hash.</param>
    /// <param name="secret">The plaintext secret to check.</param>
    /// <returns><see langword="true"/> if the secret matches; otherwise <see langword="false"/>, including when the hash is malformed.</returns>
    Task<bool> ValidateHashedSecret(string hashedSecret, string secret);
}
