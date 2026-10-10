using KnitErp.Application.Catalog;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>Страны организаций и контрагентов, справочник ставок НДС, ставка у номенклатуры (D60).</summary>
public sealed class CountryAndVatTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private static int _innSeed = 780_000_000;

    [SqlFact]
    public async Task Russian_organization_gets_rates_with_history_and_items_use_them()
    {
        var org = await CreateAsync(NextRussianInn(), "RU");
        long standard, itemId;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var list = await s.VatRates.ListAsync();
            Assert.Equal("Россия", list.CountryName);
            var main = list.Rates.Single(r => r.Kind == VatRateKind.Standard);
            Assert.Equal(22m, main.CurrentPercent);
            Assert.Equal(new DateOnly(2025, 12, 31), main.Periods[0].ValidTo);
            Assert.Contains(list.Rates, r => r.Kind == VatRateKind.Exempt && r.CurrentText == "без НДС");
            standard = main.Id;

            var unit = (await s.Catalog.ListUnitsAsync()).Single(u => u.Symbol == "кг").Id;
            itemId = await s.Catalog.CreateItemAsync(new ItemCommand("ПР-1", "Пряжа", ItemType.RawMaterial, unit, null, standard));
            var item = (await s.Catalog.ListItemsAsync(new ItemFilter())).Items.Single(i => i.Id == itemId);
            Assert.Equal("Основная", item.VatRateName);

            var rate = (await s.VatRates.ListAsync()).Rates.Single(r => r.Id == standard);
            Assert.Equal(1, rate.ItemCount);
            Assert.Equal("vat.in_use", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.VatRates.ArchiveAsync(standard, rate.RowVersion))).Code);

            // Закон меняет ставку со следующего года: новый процент, номенклатура не меняется.
            await s.VatRates.AddPeriodAsync(standard, new DateOnly(2027, 1, 1), 24m, rate.RowVersion);
            rate = (await s.VatRates.ListAsync()).Rates.Single(r => r.Id == standard);
            Assert.Equal(22m, rate.CurrentPercent);
            Assert.Equal(new DateOnly(2026, 12, 31), rate.Periods[1].ValidTo);
            Assert.Equal(24m, rate.Periods[2].Percent);

            var reduced = await s.VatRates.CreateAsync("Пониженная 5%", VatRateKind.Reduced, new DateOnly(2026, 1, 1), 5m);
            var row = (await s.Catalog.ListItemsAsync(new ItemFilter())).Items.Single(i => i.Id == itemId);
            await s.Catalog.UpdateItemAsync(itemId, new ItemCommand(row.Code, row.Name, row.Type, row.UnitId, null, reduced), row.RowVersion);
            Assert.Equal("Пониженная 5%", (await s.Catalog.ListItemsAsync(new ItemFilter())).Items.Single(i => i.Id == itemId).VatRateName);
            Assert.Contains(await s.Db.AuditEntries.Where(a => a.EntityId == itemId.ToString()).Select(a => a.Reason).ToListAsync(), r => r == "Ставка НДС");
        }

        // Ставка чужой организации — «не найдено».
        var other = await CreateAsync(NextRussianInn(), "RU");
        await using (var s = host.As(other.OwnerUserId, other.OrganizationId))
        {
            var unit = (await s.Catalog.ListUnitsAsync()).First().Id;
            await Assert.ThrowsAsync<NotFoundException>(() => s.Catalog.CreateItemAsync(new ItemCommand("X-1", "Чужая ставка", ItemType.Other, unit, null, standard)));
        }
    }

    [SqlFact]
    public async Task Kazakh_organization_gets_its_rates_currency_and_counterparty_rules()
    {
        var org = await CreateAsync("940240000498", "KZ");
        await using var s = host.As(org.OwnerUserId, org.OrganizationId);
        var dto = await s.Organizations.GetCurrentAsync();
        Assert.Equal(("KZ", "KZT", "БИН/ИИН"), (dto.CountryCode, dto.CurrencyCode, dto.Country.TaxIdName));

        var rates = (await s.VatRates.ListAsync()).Rates;
        Assert.Equal(16m, rates.Single(r => r.Kind == VatRateKind.Standard).CurrentPercent);
        Assert.Equal(5m, rates.Single(r => r.Name == "Лекарства и медизделия").CurrentPercent);

        // Контрагент без страны — страны организации; российский — с ИНН и КПП.
        var local = await s.Counterparties.CreateAsync(new CounterpartyCommand("ТОО «Жибек»", "980630000970", null, true, false, null));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Counterparties.CreateAsync(new CounterpartyCommand("ТОО «Плохой»", "7700000016", null, true, false, null)));
        await s.Counterparties.CreateAsync(new CounterpartyCommand("ООО «Пряжа»", "7700000016", "770001001", true, false, null, "RU"));
        var list = (await s.Counterparties.ListAsync(new CounterpartyFilter())).Counterparties;
        Assert.Equal("KZ", list.Single(c => c.Id == local).CountryCode);
        Assert.Equal("RU", list.Single(c => c.Name == "ООО «Пряжа»").CountryCode);

        // Загрузка: пустая страна — страна организации, страна по названию; старый шаблон без столбца «Страна» тоже годен.
        var plan = await s.CounterpartyExchange.PreviewAsync(Workbook(CounterpartyExchangeService.Columns,
            ["ТОО «Жибек»", "980630000970", "", "да", "", "", ""],
            ["ООО «Пряжа»", "7700000016", "770001001", "да", "", "", "Россия"],
            ["Минский комбинат", "190123456", "", "да", "", "", "BY"],
            ["Кто-то", "", "", "да", "", "", "Германия"]));
        Assert.Equal(ImportAction.Unchanged, plan.Rows[0].Action);
        Assert.Equal(ImportAction.Unchanged, plan.Rows[1].Action);
        Assert.Equal(ImportAction.Create, plan.Rows[2].Action);
        Assert.Equal("BY", plan.Rows[2].Cell(6));
        Assert.Contains(plan.Rows[3].Errors, e => e.Contains("не поддерживается"));

        var old = await s.CounterpartyExchange.PreviewAsync(Workbook(CounterpartyExchangeService.Columns.Take(6).ToList(),
            ["ТОО «Новый»", "100740000005", "", "да", "", ""]));
        Assert.Equal(ImportAction.Create, old.Rows.Single().Action);
        await s.CounterpartyExchange.ApplyAsync(old.Rows);
        Assert.Equal("KZ", (await s.Counterparties.ListAsync(new CounterpartyFilter())).Counterparties.Single(c => c.Name == "ТОО «Новый»").CountryCode);
    }

    private static MemoryStream Workbook(IReadOnlyList<string> columns, params IReadOnlyList<string>[] rows) =>
        new(SqlTestHost.Spreadsheet.Write([new SheetData("Лист", columns, rows)]));

    private async Task<CreatedOrganization> CreateAsync(string inn, string country)
    {
        await using var s = host.As(null, null);
        return await s.Organizations.CreateWithOwnerAsync(new CreateOrganizationCommand(
            $"Тестовая организация {inn}", $"Тест {inn}", inn, null, false,
            KnitErp.Domain.Organizations.Countries.Get(country).DefaultTimeZone, $"owner-{inn}-{Guid.NewGuid():N}@test.local", $"Владелец {inn}", country));
    }

    private static string NextRussianInn()
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
