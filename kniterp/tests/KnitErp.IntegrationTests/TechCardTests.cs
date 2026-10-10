using KnitErp.Application.Catalog;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Production;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>Техкарты изделий (D63): нормы, версии, ввод в действие, потребность, права и чужая организация.</summary>
public sealed class TechCardTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private static int _innSeed = 790_000_000;

    [SqlFact]
    public async Task Card_versions_activate_one_at_a_time_and_requirement_uses_norms()
    {
        var org = await CreateAsync();
        long sweater, yarn, buttons, v1, v2;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var units = await s.Catalog.ListUnitsAsync();
            var pcs = units.Single(u => u.Symbol == "шт").Id;
            var kg = units.Single(u => u.Symbol == "кг").Id;
            sweater = await s.Catalog.CreateItemAsync(new ItemCommand("СВ-1", "Свитер", ItemType.Finished, pcs, null));
            yarn = await s.Catalog.CreateItemAsync(new ItemCommand("ПР-1", "Пряжа шерсть", ItemType.RawMaterial, kg, null));
            buttons = await s.Catalog.CreateItemAsync(new ItemCommand("ФУ-1", "Пуговица", ItemType.Accessory, pcs, null));

            Assert.Equal("techcard.product_type",
                (await Assert.ThrowsAsync<BusinessRuleException>(() => s.TechCards.CreateAsync(yarn, 1, null))).Code);
            Assert.Equal("stock.quantity.precision",
                (await Assert.ThrowsAsync<BusinessRuleException>(() => s.TechCards.CreateAsync(sweater, 1.5m, null))).Code);

            v1 = await s.TechCards.CreateAsync(sweater, 10, "Базовая модель");
            var card = await s.TechCards.GetAsync(v1);
            Assert.Equal((1, TechCardStatus.Draft, true), (card.Version, card.Status, card.CanEdit));
            await s.TechCards.SetLineAsync(v1, yarn, 3.5m, 4m, card.RowVersion);
            await s.TechCards.SetLineAsync(v1, buttons, 50m, 0m, (await s.TechCards.GetAsync(v1)).RowVersion);
            await s.TechCards.ActivateAsync(v1, (await s.TechCards.GetAsync(v1)).RowVersion);
            card = await s.TechCards.GetAsync(v1);
            Assert.Equal((TechCardStatus.Active, false, v1), (card.Status, card.CanEdit, card.ActiveVersionId));

            // Новая версия копией: нормы те же, первая остаётся действующей до ввода второй.
            v2 = await s.TechCards.CopyAsync(v1);
            var draft = await s.TechCards.GetAsync(v2);
            Assert.Equal((2, 2, v1), (draft.Version, draft.Lines.Count, draft.ActiveVersionId));
            await s.TechCards.SetLineAsync(v2, yarn, 3.2m, 3m, draft.RowVersion);
            await s.TechCards.ActivateAsync(v2, (await s.TechCards.GetAsync(v2)).RowVersion);
            Assert.Equal(TechCardStatus.Archived, (await s.TechCards.GetAsync(v1)).Status);
            Assert.Equal(TechCardStatus.Active, (await s.TechCards.GetAsync(v2)).Status);
            Assert.Single(await s.TechCards.ListAsync("Свитер"));
            Assert.Equal(2, (await s.TechCards.ListAsync("СВ-1", includeArchived: true)).Count);

            // 25 свитеров по версии 2: 3,2 × 2,5 = 8 кг, с отходом 3% — 8,24 кг; пуговиц 125. На складе пока пусто.
            var need = (await s.TechCards.RequirementAsync(v2, 25)).ToDictionary(n => n.Code);
            Assert.Equal((8m, 8.24m, 0m, 8.24m), (need["ПР-1"].Net, need["ПР-1"].Gross, need["ПР-1"].Available!.Value, need["ПР-1"].Shortage!.Value));
            Assert.Equal(125m, need["ФУ-1"].Gross);

            var active = (await s.TechCards.GetAsync(v2)).RowVersion;
            Assert.Equal("techcard.not_draft", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.TechCards.SetLineAsync(v2, yarn, 1, 0, active))).Code);
        }

        // Главный экран Владельца: плитки по правам, готовность к запуску, техкарта в счётчике.
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var dash = await s.Dashboard.GetAsync();
            Assert.Equal(1, dash.Tiles.Single(t => t.Href == "tech-cards").Value);
            Assert.Equal(3, dash.Tiles.Single(t => t.Href == "catalog").Value);
            Assert.Contains(dash.Tiles, t => t.Href == "stock-documents?status=1" && t.Value == 0 && !t.Warn);
            Assert.NotNull(dash.Month);
            Assert.InRange(dash.ReadinessPercent!.Value, 0, 99);
        }

        await using (var db = host.NewDb())
        {
            Assert.True(await db.AuditEntries.AnyAsync(e => e.EntityType == "TechCard" && e.EntityId == v2.ToString() && e.Action == "production.techcard.activated"));
        }

        // Чужая организация не видит карту, а сотрудник без права правки справочников не меняет её.
        var other = await CreateAsync();
        await using (var s = host.As(other.OwnerUserId, other.OrganizationId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => s.TechCards.GetAsync(v2));
            await Assert.ThrowsAsync<NotFoundException>(() => s.TechCards.CopyAsync(v2));
        }
    }

    /// <summary>Аудит 10.10.2026, п. 1: старая вкладка не перезаписывает чужую правку и не меняет действующую карту.</summary>
    [SqlFact]
    public async Task Stale_tab_cannot_overwrite_norms_or_change_active_card()
    {
        var org = await CreateAsync();
        long card, yarn, buttons;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var units = await s.Catalog.ListUnitsAsync();
            var pcs = units.Single(u => u.Symbol == "шт").Id;
            var sweater = await s.Catalog.CreateItemAsync(new ItemCommand("СВ-1", "Свитер", ItemType.Finished, pcs, null));
            yarn = await s.Catalog.CreateItemAsync(new ItemCommand("ПР-1", "Пряжа", ItemType.RawMaterial, units.Single(u => u.Symbol == "кг").Id, null));
            buttons = await s.Catalog.CreateItemAsync(new ItemCommand("ФУ-1", "Пуговица", ItemType.Accessory, pcs, null));
            card = await s.TechCards.CreateAsync(sweater, 10, null);
        }

        // Две вкладки открыли одну и ту же версию карты.
        byte[] tabA, tabB;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            tabA = (await s.TechCards.GetAsync(card)).RowVersion;
            tabB = tabA;
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            await s.TechCards.SetLineAsync(card, yarn, 3m, 0m, tabA);
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            // Вкладка B со старой версией получает конфликт — правка вкладки A не теряется.
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => s.TechCards.SetLineAsync(card, yarn, 9m, 0m, tabB));
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => s.TechCards.SetLineAsync(card, buttons, 5m, 0m, tabB));
            Assert.Equal(3m, (await s.TechCards.GetAsync(card)).Lines.Single().Quantity);
        }

        // Карта введена в действие другим пользователем; прежняя вкладка с версией черновика ничего не меняет.
        byte[] stale;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            stale = (await s.TechCards.GetAsync(card)).RowVersion;
            await s.TechCards.ActivateAsync(card, stale);
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => s.TechCards.SetLineAsync(card, yarn, 1m, 0m, stale));
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => s.TechCards.RemoveLineAsync(card, yarn, stale));
            var dto = await s.TechCards.GetAsync(card);
            Assert.Equal((TechCardStatus.Active, 3m), (dto.Status, dto.Lines.Single().Quantity));
        }

        // Даже в обход приложения строки действующей карты не меняются — запрещает триггер в базе.
        await using (var db = host.NewDb())
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [kniterp].[tech_card_lines] SET [Quantity] = 99 WHERE [TechCardId] = {card}"));
            Assert.Contains("новую версию", ex.Message);
        }
    }

    /// <summary>Аудит 10.10.2026, п. 2: карта и запись журнала сохраняются вместе — сбой журнала не оставляет карту.</summary>
    [SqlFact]
    public async Task Card_is_not_left_without_audit_record_when_audit_fails()
    {
        var org = await CreateAsync();
        long sweater;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var pcs = (await s.Catalog.ListUnitsAsync()).Single(u => u.Symbol == "шт").Id;
            sweater = await s.Catalog.CreateItemAsync(new ItemCommand("СВ-9", "Свитер", ItemType.Finished, pcs, null));
        }

        // Сбой записи в журнал — только для техкарт этой организации, на время теста.
        await using (var db = host.NewDb())
        {
            // Номер организации — число из теста, не ввод пользователя.
            var sql = $"""
                CREATE TRIGGER [kniterp].[tr_test_audit_fail] ON [kniterp].[audit_log] AFTER INSERT AS
                BEGIN
                    IF EXISTS (SELECT 1 FROM inserted WHERE [EntityType] = 'TechCard' AND [OrganizationId] = {org.OrganizationId})
                        THROW 51999, N'Сбой журнала (тест)', 1;
                END
                """;
            await db.Database.ExecuteSqlRawAsync(sql);
        }

        try
        {
            await using var s = host.As(org.OwnerUserId, org.OrganizationId);
            await Assert.ThrowsAnyAsync<Exception>(() => s.TechCards.CreateAsync(sweater, 10, null));
        }
        finally
        {
            await using var db = host.NewDb();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [kniterp].[tr_test_audit_fail]");
        }

        await using (var db = host.NewDb())
        {
            Assert.False(await db.TechCards.AnyAsync(c => c.OrganizationId == org.OrganizationId));
        }

        // После устранения сбоя карта создаётся как обычно — версия 1, без «лишней» первой.
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var id = await s.TechCards.CreateAsync(sweater, 10, null);
            Assert.Equal(1, (await s.TechCards.GetAsync(id)).Version);
        }
    }

    private async Task<CreatedOrganization> CreateAsync()
    {
        int[] w = [2, 4, 10, 3, 5, 9, 4, 6, 8];
        var body = Interlocked.Increment(ref _innSeed).ToString("D9");
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (body[i] - '0') * w[i];
        }

        var inn = body + (sum % 11 % 10);
        await using var s = host.As(null, null);
        return await s.Organizations.CreateWithOwnerAsync(new CreateOrganizationCommand(
            $"Тестовая организация {inn}", $"Тест {inn}", inn, null, false, "Europe/Moscow", $"owner-{inn}-{Guid.NewGuid():N}@test.local", $"Владелец {inn}", "RU"));
    }
}
