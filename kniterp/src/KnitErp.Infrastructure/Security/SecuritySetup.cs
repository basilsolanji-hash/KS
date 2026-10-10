using System.Xml.Linq;
using KnitErp.Domain.Access;
using KnitErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Infrastructure.Security;

/// <summary>Откуда взялся мастер-ключ — для журнала запуска (сам ключ не пишется никуда).</summary>
public sealed record MasterKeySource(KeyRing Ring, string Description);

public static class SecuritySetup
{
    /// <summary>
    /// Мастер-ключ из настроек (Security:MasterKey, прежние — Security:PreviousMasterKeys через запятую).
    /// Рабочая среда без ключа не запускается. Для разработки ключ создаётся один раз в профиле пользователя
    /// (~/.kniterp/dev-master.key) — вне репозитория; при его потере зашифрованные секреты 2FA на этом компьютере
    /// придётся сбросить.
    /// </summary>
    public static MasterKeySource LoadMasterKey(string? masterKey, string? previousKeys, bool development)
    {
        var previous = (previousKeys ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => KeyRing.Parse(k, "Security:PreviousMasterKeys")).ToList();
        if (!string.IsNullOrWhiteSpace(masterKey))
        {
            var ring = new KeyRing(KeyRing.Parse(masterKey, "Security:MasterKey"), previous);
            return new MasterKeySource(ring, $"мастер-ключ из настроек, отпечаток {ring.CurrentId}");
        }

        if (!development)
        {
            throw new InvalidOperationException(
                "Не задан мастер-ключ Security:MasterKey. Создайте ключ («dotnet KnitErp.Web.dll new-master-key») и положите его в секреты сервера — не в репозиторий и не в базу.");
        }

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kniterp");
        var file = Path.Combine(folder, "dev-master.key");
        if (!File.Exists(file))
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(file, KeyRing.NewKeyBase64());
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        var dev = new KeyRing(KeyRing.Parse(File.ReadAllText(file), file), previous);
        return new MasterKeySource(dev, $"ключ разработки из {file}, отпечаток {dev.CurrentId}");
    }

    /// <summary>
    /// Рабочая среда разговаривает с SQL Server только по шифрованному каналу (TLS): иначе пароль базы и данные идут
    /// по сети открыто. Исключение — явная настройка Security:AllowUnencryptedDatabase=true (база на том же сервере).
    /// </summary>
    public static void EnsureEncryptedConnection(string connectionString, bool allowUnencrypted)
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
        if (builder.Encrypt == Microsoft.Data.SqlClient.SqlConnectionEncryptOption.Optional && !allowUnencrypted)
        {
            throw new InvalidOperationException(
                "Соединение с базой не шифруется (Encrypt=false). Уберите Encrypt=false из строки подключения или, если база на том же сервере, задайте Security:AllowUnencryptedDatabase=true.");
        }
    }

    /// <summary>
    /// Секреты 2FA, записанные до включения шифрования, шифруются при обновлении схемы. Возвращает число записей.
    /// </summary>
    public static async Task<int> EncryptLegacySecretsAsync(KnitErpDbContext db, CancellationToken ct = default)
    {
        var ids = await db.Database
            .SqlQuery<long>($"SELECT [Id] AS [Value] FROM [kniterp].[users] WHERE [AuthenticatorKey] IS NOT NULL AND [AuthenticatorKey] NOT LIKE 'enc:%'")
            .ToListAsync(ct);
        if (ids.Count == 0)
        {
            return 0;
        }

        var users = await db.Users.Where(u => ids.Contains(u.Id)).ToListAsync(ct);
        foreach (var user in users)
        {
            db.Entry(user).Property(nameof(UserAccount.AuthenticatorKey)).IsModified = true;
        }

        await db.SaveChangesAsync(ct);
        return users.Count;
    }
}

/// <summary>Шифрует XML ключей защиты cookie мастер-ключом перед записью в базу.</summary>
public sealed class MasterKeyXmlEncryptor : IXmlEncryptor
{
    public EncryptedXmlInfo Encrypt(XElement plaintextElement) =>
        new(new XElement("encryptedKey", new XComment(" Зашифровано мастер-ключом knitERP (AES-256-GCM). "),
            new XElement("value", SecretCipher.Encrypt(plaintextElement.ToString(SaveOptions.DisableFormatting)))), typeof(MasterKeyXmlDecryptor));
}

public sealed class MasterKeyXmlDecryptor : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement) =>
        XElement.Parse(SecretCipher.Decrypt((string?)encryptedElement.Element("value") ?? throw new InvalidOperationException("Пустой ключ защиты.")));
}
