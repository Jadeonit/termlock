using System.Security.Cryptography;
using System.Text;

namespace TermLock.Core;

/// <summary>
/// 160-bit recovery code, Crockford base32, shown once and stored as SHA-256.
/// Format: XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX.
/// </summary>
public static class RecoveryCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int GroupSize = 4;
    private const int GroupCount = 8;
    private const int TokenBytes = 20;

    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        var chars = new char[32];
        var buffer = 0;
        var bits = 0;
        var index = 0;

        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value;
            bits += 8;

            while (bits >= 5)
            {
                bits -= 5;
                chars[index++] = Alphabet[(buffer >> bits) & 31];
            }
        }

        var text = new string(chars);
        var groups = new string[GroupCount];

        for (var i = 0; i < GroupCount; i++)
        {
            groups[i] = text.Substring(i * GroupSize, GroupSize);
        }

        return string.Join("-", groups);
    }

    public static string Normalize(string code)
    {
        var builder = new StringBuilder(code.Length);

        foreach (var raw in code.ToUpperInvariant())
        {
            if (raw is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9')))
            {
                continue;
            }

            var mapped = raw switch
            {
                'I' => '1',
                'L' => '1',
                'O' => '0',
                _ => raw
            };

            if (Alphabet.Contains(mapped))
            {
                builder.Append(mapped);
            }
        }

        return builder.ToString();
    }

    public static string Hash(string code)
    {
        var normalized = Normalize(code);
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public static bool Verify(string code, string hashBase64)
    {
        try
        {
            var expected = Convert.FromBase64String(hashBase64);
            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code)));
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }
}
