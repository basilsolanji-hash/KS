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
