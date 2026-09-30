using System.Security.Cryptography;
using System.Text;

namespace Rostok.Core.Storage;

// Пароли пользователей: в базе только соль и хеш PBKDF2-SHA256.
public static class Passwords
{
    public const int Iterations = 210_000;
    public const int MinLength = 4;

    public static string? Validate(string password, string repeat)
    {
        if (password.Length < MinLength) return $"Пароль — не короче {MinLength} символов.";
        if (password != repeat) return "Пароли не совпадают.";
        return null;
    }

    public static (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Matches(string password, string hash, string salt, int iterations)
    {
        if (hash.Length == 0 || salt.Length == 0) return false;
        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Convert.FromBase64String(salt), iterations, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(hash));
    }
}
