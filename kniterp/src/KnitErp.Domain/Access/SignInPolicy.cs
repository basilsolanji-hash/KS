using System.Security.Cryptography;
using System.Text;
using KnitErp.Domain.Common;

namespace KnitErp.Domain.Access;

/// <summary>Параметры входа. Значения — рабочие допущения, пока заказчик не утвердил политику (docs/decisions.md).</summary>
public static class SignInPolicy
{
    /// <summary>Срок ссылки приглашения — 72 часа (план ядра доступа).</summary>
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromHours(72);

    // Допущение D25: 5 неудачных попыток подряд блокируют вход на 15 минут.
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // Допущение D26: длина пароля 10–128 символов, без требований к составу (NIST SP 800-63B).
    public const int PasswordMinLength = 10;
    public const int PasswordMaxLength = 128;

    /// <summary>Роли, которым вход без второго фактора запрещён.</summary>
    public static bool RequiresTwoFactor(string roleCode) => SystemRoles.IsAdministrative(roleCode);

    public static void EnsurePasswordAcceptable(string? password, string email)
    {
        if (password is null || password.Length < PasswordMinLength)
        {
            throw new BusinessRuleException("auth.password.too_short", $"Пароль должен быть не короче {PasswordMinLength} символов.");
        }

        if (password.Length > PasswordMaxLength)
        {
            throw new BusinessRuleException("auth.password.too_long", $"Пароль должен быть не длиннее {PasswordMaxLength} символов.");
        }

        if (string.Equals(password.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException("auth.password.equals_email", "Пароль не должен совпадать с email.");
        }

        if (password.Distinct().Count() < 3)
        {
            throw new BusinessRuleException("auth.password.too_simple", "Пароль слишком простой.");
        }
    }
}

/// <summary>Одноразовые токены ссылок. В базе хранится только SHA-256.</summary>
public static class SetupTokens
{
    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty));

    public static bool Matches(string? token, byte[] expectedHash) =>
        CryptographicOperations.FixedTimeEquals(Hash(token ?? string.Empty), expectedHash);
}
