using System.Security.Cryptography;
using System.Text;

namespace KnitErp.Domain.Access;

/// <summary>
/// Резервный код входа (D06): заменяет код из приложения-аутентификатора, если телефон потерян. Код одноразовый,
/// в базе хранится только SHA-256 от нормализованного кода. Набор выдаётся пользователю один раз; новый набор заменяет старый.
/// </summary>
public sealed class RecoveryCode
{
    public const int SetSize = 10;

    // Без похожих символов (0/O, 1/I/L): код переписывают с бумаги.
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    private const int Length = 12;

    private RecoveryCode()
    {
    }

    public long Id { get; private set; }
    public long UserId { get; private set; }
    public byte[] CodeHash { get; private set; } = [];
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UsedAtUtc { get; private set; }

    /// <summary>Новый набор: сами коды (показать один раз) и записи для базы.</summary>
    public static (IReadOnlyList<string> Codes, IReadOnlyList<RecoveryCode> Records) Issue(long userId, DateTime nowUtc)
    {
        var codes = Enumerable.Range(0, SetSize).Select(_ => Generate()).ToList();
        return (codes, codes.Select(c => new RecoveryCode { UserId = userId, CodeHash = Hash(c), CreatedAtUtc = nowUtc }).ToList());
    }

    public void MarkUsed(DateTime nowUtc) => UsedAtUtc ??= nowUtc;

    /// <summary>Похоже ли введённое на резервный код (а не на 6 цифр из приложения).</summary>
    public static bool LooksLikeCode(string? input) => Normalize(input).Length == Length;

    public static byte[] Hash(string? code) => SHA256.HashData(Encoding.ASCII.GetBytes(Normalize(code)));

    /// <summary>Без дефисов и пробелов, заглавными.</summary>
    public static string Normalize(string? code) =>
        new((code ?? string.Empty).ToUpperInvariant().Where(c => c is not ('-' or ' ')).ToArray());

    private static string Generate()
    {
        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        var s = new string(chars);
        return $"{s[..4]}-{s[4..8]}-{s[8..]}";
    }
}
