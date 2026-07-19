using System.Security.Cryptography;

namespace PakkaHisaab.Infrastructure.Auth;

/// <summary>PBKDF2-SHA256 password hashing (100k iterations, 16-byte salt), constant-time verify.</summary>
public static class PasswordHasher
{
    const int Iterations = 100_000;
    const int SaltSize = 16;
    const int KeySize = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2") return false;

        int iterations = int.Parse(parts[1]);
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
