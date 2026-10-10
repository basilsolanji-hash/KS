using System.Security.Cryptography;
using System.Text;

namespace KnitErp.Infrastructure.Security;

/// <summary>
/// Мастер-ключ системы (32 байта) и прежние ключи для чтения старых данных после смены ключа.
/// Ключ задаётся вне базы и репозитория — в секретах сервера (Security:MasterKey). Из него выводятся отдельные
/// ключи: для шифрования секретов (AES-256-GCM) и для подписи журналов (HMAC-SHA256). Кто украл только копию базы,
/// не расшифрует секреты и не подделает подпись записей.
/// </summary>
public sealed class KeyRing
{
    public const int KeyLength = 32;

    private static KeyRing? _current;

    private readonly List<(string Id, byte[] Secrets, byte[] Integrity)> _keys;

    public KeyRing(byte[] current, IEnumerable<byte[]>? previous = null)
    {
        _keys = [Derive(current)];
        foreach (var key in previous ?? [])
        {
            _keys.Add(Derive(key));
        }
    }

    /// <summary>Ключи процесса. Задаются один раз при запуске (Program, тесты); без них шифрование и подпись невозможны.</summary>
    public static KeyRing Current =>
        _current ?? throw new InvalidOperationException("Мастер-ключ не задан: укажите Security:MasterKey в секретах сервера.");

    public static bool IsConfigured => _current is not null;

    public static void Configure(KeyRing ring) => _current = ring;

    /// <summary>Короткий отпечаток ключа: по нему видно, каким ключом зашифровано значение. Сам ключ из него не восстановить.</summary>
    public string CurrentId => _keys[0].Id;

    internal byte[] SecretsKey(string id) =>
        _keys.FirstOrDefault(k => k.Id == id).Secrets ?? throw new CryptographicException($"Нет ключа {id} — добавьте прежний ключ в Security:PreviousMasterKeys.");

    internal byte[] CurrentSecretsKey => _keys[0].Secrets;

    /// <summary>Ключи подписи: текущий первым, затем прежние — проверка пробует их по очереди.</summary>
    internal IReadOnlyList<byte[]> IntegrityKeys => _keys.Select(k => k.Integrity).ToList();

    public static byte[] Parse(string base64, string source)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{source}: ключ должен быть в Base64.");
        }

        return key.Length == KeyLength ? key : throw new InvalidOperationException($"{source}: нужен ключ длиной {KeyLength} байта (44 символа Base64).");
    }

    public static string NewKeyBase64() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyLength));

    private static (string, byte[], byte[]) Derive(byte[] master)
    {
        if (master.Length != KeyLength)
        {
            throw new ArgumentException($"Нужен ключ {KeyLength} байта.", nameof(master));
        }

        var id = Convert.ToHexString(SHA256.HashData(master))[..8].ToLowerInvariant();
        var secrets = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, KeyLength, info: Encoding.UTF8.GetBytes("kniterp.secrets.v1"));
        var integrity = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, KeyLength, info: Encoding.UTF8.GetBytes("kniterp.integrity.v1"));
        return (id, secrets, integrity);
    }
}

/// <summary>
/// Шифрование отдельных значений (секреты 2FA, ключи защиты cookie): AES-256-GCM, случайный nonce на каждое значение,
/// формат «enc:v1:отпечаток:Base64(nonce|tag|шифртекст)». GCM обнаруживает любое изменение шифртекста.
/// </summary>
public static class SecretCipher
{
    public const string Prefix = "enc:v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static bool IsEncrypted(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    public static string Encrypt(string plaintext) => Encrypt(plaintext, KeyRing.Current);

    public static string Encrypt(string plaintext, KeyRing ring)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var buffer = new byte[NonceSize + TagSize + data.Length];
        var nonce = buffer.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(ring.CurrentSecretsKey, TagSize);
        aes.Encrypt(nonce, data, buffer.AsSpan(NonceSize + TagSize), buffer.AsSpan(NonceSize, TagSize), Aad(ring.CurrentId));
        return $"{Prefix}{ring.CurrentId}:{Convert.ToBase64String(buffer)}";
    }

    /// <summary>Значение без префикса — старое незашифрованное: возвращается как есть и шифруется при следующей записи.</summary>
    public static string Decrypt(string stored) => Decrypt(stored, KeyRing.Current);

    public static string Decrypt(string stored, KeyRing ring)
    {
        if (!IsEncrypted(stored))
        {
            return stored;
        }

        var rest = stored[Prefix.Length..];
        var colon = rest.IndexOf(':');
        var keyId = rest[..colon];
        var buffer = Convert.FromBase64String(rest[(colon + 1)..]);
        var plain = new byte[buffer.Length - NonceSize - TagSize];
        using var aes = new AesGcm(ring.SecretsKey(keyId), TagSize);
        aes.Decrypt(buffer.AsSpan(0, NonceSize), buffer.AsSpan(NonceSize + TagSize), buffer.AsSpan(NonceSize, TagSize), plain, Aad(keyId));
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] Aad(string keyId) => Encoding.UTF8.GetBytes("kniterp:" + keyId);
}
