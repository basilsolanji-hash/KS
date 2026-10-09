using System.Security.Cryptography;
using KnitErp.Application.Access;
using KnitErp.Application.Catalog;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Application.Warehousing;
using KnitErp.Domain.Access;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Warehousing;
using KnitErp.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>Защита данных: шифрование секретов 2FA, подпись журнала и движений, обнаружение вмешательства.</summary>
public sealed class DataSecurityTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private static int _innSeed = 740_000_000;

    [SqlFact]
    public async Task Authenticator_secret_is_stored_encrypted_and_legacy_plaintext_is_encrypted_on_upgrade()
    {
        var f = await SetUpAsync();
        string key;
        await using (var db = host.NewDb())
        {
            var user = await db.Users.SingleAsync(u => u.Id == f.Keeper);
            key = user.BeginAuthenticatorSetup();
            await db.SaveChangesAsync();
        }

        Assert.StartsWith("enc:v1:", await RawSecretAsync(f.Keeper));
        Assert.DoesNotContain(key, await RawSecretAsync(f.Keeper));
        await using (var db = host.NewDb())
        {
            Assert.Equal(key, (await db.Users.SingleAsync(u => u.Id == f.Keeper)).AuthenticatorKey);
        }

        // Секрет, записанный до шифрования, шифруется при обновлении схемы.
        await using (var db = host.NewDb())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [kniterp].[users] SET [AuthenticatorKey] = {"JBSWY3DPEHPK3PXP"} WHERE [Id] = {f.Keeper}");
            Assert.Equal("JBSWY3DPEHPK3PXP", (await db.Users.AsNoTracking().SingleAsync(u => u.Id == f.Keeper)).AuthenticatorKey);
            Assert.True(await SecuritySetup.EncryptLegacySecretsAsync(db) >= 1);
        }

        Assert.StartsWith("enc:v1:", await RawSecretAsync(f.Keeper));
        await using (var db = host.NewDb())
        {
            Assert.Equal("JBSWY3DPEHPK3PXP", (await db.Users.SingleAsync(u => u.Id == f.Keeper)).AuthenticatorKey);
        }
    }

    [SqlFact]
    public async Task Integrity_check_passes_and_detects_change_deletion_and_bypass_insert()
    {
        var f = await SetUpAsync();
        await PostReceiptAsync(f, 10m);
        await PostReceiptAsync(f, 5m);

        await using (var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId))
        {
            var report = await s.Integrity.VerifyAsync();
            Assert.All(report, r => Assert.True(r.Ok, string.Join("; ", r.Problems.Select(p => p.Description))));
            Assert.Equal(2, report.Single(r => r.Journal == "Движения склада").Checked);
            Assert.True(report.Single(r => r.Journal == "Журнал аудита").Checked > 5);
            Assert.NotNull(report[0].Fingerprint);
        }

        long firstMovement, auditMiddle;
        await using (var db = host.NewDb())
        {
            firstMovement = await db.StockMovements.Where(m => m.OrganizationId == f.Org.OrganizationId).MinAsync(m => m.Id);
            var audits = await db.AuditEntries.Where(a => a.OrganizationId == f.Org.OrganizationId).OrderBy(a => a.Id).Select(a => a.Id).ToListAsync();
            auditMiddle = audits[3];

            // Администратор базы отключает защиту и правит данные напрямую.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DISABLE TRIGGER [kniterp].[tr_stock_movements_immutable] ON [kniterp].[stock_movements];
                UPDATE [kniterp].[stock_movements] SET [Quantity] = 1000 WHERE [Id] = {firstMovement};
                ENABLE TRIGGER [kniterp].[tr_stock_movements_immutable] ON [kniterp].[stock_movements];
                DISABLE TRIGGER [kniterp].[tr_audit_log_immutable] ON [kniterp].[audit_log];
                DELETE FROM [kniterp].[audit_log] WHERE [Id] = {auditMiddle};
                ENABLE TRIGGER [kniterp].[tr_audit_log_immutable] ON [kniterp].[audit_log];
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [kniterp].[stock_movements] ([OrganizationId], [WarehouseId], [ItemId], [Quantity], [OccurredOn], [Source], [SourceId], [CreatedAtUtc])
                VALUES ({f.Org.OrganizationId}, {f.Yarn}, {f.Wool}, 50, {new DateTime(2026, 10, 2)}, 2, 0, {host.Clock.UtcNow})
                """);
        }

        await using (var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId))
        {
            var report = await s.Integrity.VerifyAsync();
            var stock = report.Single(r => r.Journal == "Движения склада");
            Assert.Contains(stock.Problems, p => p.RecordId == firstMovement && p.Description.StartsWith("Подпись не сходится"));
            Assert.Contains(stock.Problems, p => p.Description.StartsWith("Запись без подписи"));
            Assert.Contains(report.Single(r => r.Journal == "Журнал аудита").Problems, p => p.Description.StartsWith("Удалена запись цепочки"));
        }

        // Проверка видна в журнале; кладовщику она недоступна.
        await using (var s = host.As(f.Keeper, f.Org.OrganizationId))
        {
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Integrity.VerifyAsync());
        }
    }

    [SqlFact]
    public async Task Parallel_writes_keep_the_chain_valid()
    {
        var f = await SetUpAsync();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
        {
            await using var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId);
            await s.Catalog.CreateItemAsync(new ItemCommand($"П-{i}", $"Позиция {i}", ItemType.Other,
                (await s.Catalog.ListUnitsAsync()).Single(u => u.Symbol == "шт").Id, null));
        }));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostReceiptAsync(f, 1m)));

        await using var check = host.As(f.Org.OwnerUserId, f.Org.OrganizationId);
        Assert.All(await check.Integrity.VerifyAsync(), r => Assert.True(r.Ok, string.Join("; ", r.Problems.Select(p => $"{p.RecordId}: {p.Description}"))));
    }

    [Fact]
    public void Secrets_survive_master_key_rotation_and_detect_tampering()
    {
        var oldKey = RandomNumberGenerator.GetBytes(32);
        var newKey = RandomNumberGenerator.GetBytes(32);
        var stored = SecretCipher.Encrypt("JBSWY3DPEHPK3PXP", new KeyRing(oldKey));

        var rotated = new KeyRing(newKey, [oldKey]);
        Assert.Equal("JBSWY3DPEHPK3PXP", SecretCipher.Decrypt(stored, rotated));
        Assert.Contains(rotated.CurrentId, SecretCipher.Encrypt("x", rotated));
        Assert.ThrowsAny<CryptographicException>(() => SecretCipher.Decrypt(stored, new KeyRing(newKey)));

        var bytes = Convert.FromBase64String(stored[(stored.LastIndexOf(':') + 1)..]);
        bytes[^1] ^= 1;
        var tampered = stored[..(stored.LastIndexOf(':') + 1)] + Convert.ToBase64String(bytes);
        Assert.ThrowsAny<CryptographicException>(() => SecretCipher.Decrypt(tampered, new KeyRing(oldKey)));
    }

    [Theory]
    [InlineData("Server=db;Database=k;User Id=u;Password=p;Encrypt=false", false, true)]
    [InlineData("Server=db;Database=k;User Id=u;Password=p;Encrypt=false", true, false)]
    [InlineData("Server=db;Database=k;User Id=u;Password=p", false, false)]
    [InlineData("Server=db;Database=k;User Id=u;Password=p;Encrypt=strict", false, false)]
    public void Production_requires_encrypted_database_connection(string connection, bool allow, bool throws)
    {
        var ex = Record.Exception(() => SecuritySetup.EnsureEncryptedConnection(connection, allow));
        Assert.Equal(throws, ex is InvalidOperationException);
    }

    private async Task<string> RawSecretAsync(long userId)
    {
        await using var db = host.NewDb();
        return await db.Database.SqlQuery<string>($"SELECT [AuthenticatorKey] AS [Value] FROM [kniterp].[users] WHERE [Id] = {userId}").SingleAsync();
    }

    private async Task PostReceiptAsync(Fixture f, decimal qty)
    {
        await using var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId);
        var reason = (await s.Documents.GetOptionsAsync(StockOperationKind.Receipt)).Reasons[0].Id;
        var doc = await s.Documents.CreateAsync(StockOperationKind.Receipt, new StockDocumentHeader(f.Yarn, null, null, reason, new DateOnly(2026, 10, 1), null));
        await s.Documents.SetLineAsync(doc, f.Wool, qty, (await s.Documents.GetAsync(doc)).RowVersion);
        await s.Documents.PostAsync(doc, (await s.Documents.GetAsync(doc)).RowVersion);
    }

    private sealed record Fixture(CreatedOrganization Org, long Yarn, long Wool, long Keeper);

    private async Task<Fixture> SetUpAsync()
    {
        var inn = NextValidInn();
        CreatedOrganization org;
        await using (var s = host.As(null, null))
        {
            org = await s.Organizations.CreateWithOwnerAsync(new CreateOrganizationCommand(
                $"Тестовая организация {inn}", $"Тест {inn}", inn, null, false, "Europe/Moscow", $"owner-{inn}@test.local", $"Владелец {inn}"));
        }

        long yarn, wool, keeper;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var units = await s.Catalog.ListUnitsAsync();
            yarn = await s.Warehouses.CreateWarehouseAsync("Склад пряжи", null);
            wool = await s.Catalog.CreateItemAsync(new ItemCommand("ПР-1", "Пряжа шерсть", ItemType.RawMaterial, units.Single(u => u.Symbol == "кг").Id, null));
            keeper = (await s.Access.InviteAsync(new InviteUserCommand($"keeper-{Guid.NewGuid():N}@test.local",
                "Кладовщик", SystemRoles.Storekeeper, null, WarehouseId: yarn))).UserId;
        }

        await using var db = host.NewDb();
        (await db.Users.SingleAsync(u => u.Id == keeper)).Activate();
        await db.SaveChangesAsync();
        return new Fixture(org, yarn, wool, keeper);
    }

    private static string NextValidInn()
    {
        int[] w = [2, 4, 10, 3, 5, 9, 4, 6, 8];
        var body = Interlocked.Increment(ref _innSeed).ToString("D9");
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (body[i] - '0') * w[i];
        }

        return body + (sum % 11 % 10);
    }
}
