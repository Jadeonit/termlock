using System.Security.Cryptography;

namespace TermLock.Core;

/// <summary>
/// PBKDF2-HMAC-SHA256 passphrase hashing.
///
/// Why not store the passphrase? Because a parent (or a curious guest) can open
/// %APPDATA%\TermLock\config.json. They will find a salt and a hash, not the phrase.
///
/// 600k iterations is the OWASP 2023 recommendation for PBKDF2-HMAC-SHA256.
/// It costs ~100-200 ms on a modern laptop, which is fine for a human typing a
/// passphrase but expensive for an offline brute-force run.
/// </summary>
public static class Passphrase
{
    public const int SaltSize = 16;
    public const int HashSize = 32;
    public const int DefaultIterations = 600_000;

    public static (string Hash, string Salt, int Iterations) Create(
        string passphrase,
        int iterations = DefaultIterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            passphrase,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt), iterations);
    }

    public static bool Verify(
        string passphrase,
        string hashBase64,
        string saltBase64,
        int iterations)
    {
        try
        {
            var expected = Convert.FromBase64String(hashBase64);
            var salt = Convert.FromBase64String(saltBase64);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                passphrase,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);

            // Constant-time comparison: do not leak how many bytes matched.
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }
}
