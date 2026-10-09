using System.Security.Cryptography;
using System.Text;

namespace KnitErp.Domain.Access;

/// <summary>
/// Одноразовые коды TOTP (RFC 6238: HMAC-SHA1, 6 цифр, шаг 30 секунд) — совместимы с Яндекс Ключом,
/// Google Authenticator и другими приложениями. Допускается расхождение часов на один шаг в обе стороны.
/// </summary>
public static class Totp
{
    public const int Digits = 6;
    public const int StepSeconds = 30;
    private const int AllowedDriftSteps = 1;

    public static string NewKey() => Base32.Encode(RandomNumberGenerator.GetBytes(20));

    public static long StepAt(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds() / StepSeconds;

    public static string Compute(string base32Key, long step, int digits = Digits)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(Base32.Decode(base32Key), counter, hash);

        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        var modulo = (int)Math.Pow(10, digits);
        return (binary % modulo).ToString(new string('0', digits));
    }

    /// <summary>Шаг, которому соответствует код, или null.</summary>
    public static long? FindMatchingStep(string base32Key, string? code, DateTime nowUtc)
    {
        var normalized = (code ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);
        if (normalized.Length != Digits || !normalized.All(char.IsAsciiDigit))
        {
            return null;
        }

        var current = StepAt(nowUtc);
        long? match = null;
        for (var step = current - AllowedDriftSteps; step <= current + AllowedDriftSteps; step++)
        {
            // Проверяем все шаги окна без раннего выхода, чтобы время ответа не зависело от совпадения.
            var candidate = Encoding.ASCII.GetBytes(Compute(base32Key, step));
            if (CryptographicOperations.FixedTimeEquals(candidate, Encoding.ASCII.GetBytes(normalized)))
            {
                match = step;
            }
        }

        return match;
    }

    /// <summary>Ссылка otpauth:// для QR-кода приложения-аутентификатора.</summary>
    public static string ProvisioningUri(string issuer, string account, string base32Key) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={base32Key}&issuer={Uri.EscapeDataString(issuer)}&digits={Digits}&period={StepSeconds}";
}

/// <summary>Base32 по RFC 4648 без выравнивания «=» — формат секретов приложений-аутентификаторов.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return sb.ToString();
    }

    public static byte[] Decode(string text)
    {
        var clean = text.TrimEnd('=').Replace(" ", string.Empty).ToUpperInvariant();
        var result = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0)
            {
                throw new FormatException($"Недопустимый символ Base32: {c}.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                result.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return result.ToArray();
    }
}
