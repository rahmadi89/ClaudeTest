using System.Security.Cryptography;
using System.Text;

namespace AtmMonitor.Infrastructure.Security;

/// <summary>
/// Hashing for machine-generated, high-entropy secrets (agent keys, enrollment tokens).
/// These are 256-bit random values, so a fast hash (SHA-256) is appropriate; slow KDFs are only needed for
/// human-chosen passwords (handled by <c>PasswordHasher&lt;User&gt;</c>).
/// </summary>
public static class SecretHasher
{
    public static string GenerateSecret(int bytes = 32) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public static bool Verify(string secret, string? expectedHash)
    {
        if (string.IsNullOrEmpty(expectedHash))
        {
            return false;
        }

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
