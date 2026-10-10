using KnitErp.Application.Access;
using KnitErp.Application.Catalog;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Application.Sales;
using KnitErp.Domain.Access;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Organizations;
using KnitErp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>Номенклатура (D79): группы, карточка, штрихкоды, виды цен, доп. поля и свои справочники, права и чужая организация.</summary>
public sealed class NomenclatureTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private static int _innSeed = 820_000_000;
    private static readonly DateOnly Day = new(2026, 10, 10);

    [SqlFact]
    public async Task Groups_card_barcodes_prices_and_user_catalogs()
    {
        var org = await CreateOrgAsync();
        long yarn, wool, merino, item, other, colors, blue, colorField, priceType;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            // Группы: дерево, глубина, уникальность, циклы.
            yarn = await s.Nomenclature.CreateGroupAsync(null, "Пряжа");
            wool = await s.Nomenclature.CreateGroupAsync(yarn, "Шерсть");
            merino = await s.Nomenclature.CreateGroupAsync(wool, "Меринос");
            Assert.Equal("catalog.group.duplicate", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Nomenclature.CreateGroupAsync(yarn, "Шерсть"))).Code);
            await s.Nomenclature.CreateGroupAsync(null, "Шерсть"); // в корне — можно
            var tree = await s.Nomenclature.GroupsAsync();
            Assert.Equal("Пряжа / Шерсть / Меринос", tree.Single(g => g.Id == merino).Path);
            Assert.Equal("catalog.group.cycle", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Nomenclature.MoveGroupAsync(yarn, merino, tree.Single(g => g.Id == yarn).RowVersion))).Code);
            long? deep = merino;
            for (var i = 0; i < 2; i++)
            {
                deep = await s.Nomenclature.CreateGroupAsync(deep, $"Уровень {i + 4}");
            }

            Assert.Equal("catalog.group.depth", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Nomenclature.CreateGroupAsync(deep, "Шестой"))).Code);

            // Позиция в группе; список по группе с подгруппами, поиск по артикулу и штрихкоду.
            var unit = (await s.Catalog.ListUnitsAsync()).Single(u => u.Symbol == "кг").Id;
            item = await s.Catalog.CreateItemAsync(new ItemCommand("ПР-1", "Пряжа меринос 2/48", ItemType.RawMaterial, unit, null, GroupId: merino));
            other = await s.Catalog.CreateItemAsync(new ItemCommand("ПР-2", "Пряжа хлопок", ItemType.RawMaterial, unit, null));
            Assert.Equal([item], (await s.Catalog.ListItemsAsync(new ItemFilter(GroupId: yarn))).Items.Select(i => i.Id));
            Assert.Equal([other], (await s.Catalog.ListItemsAsync(new ItemFilter(GroupId: 0))).Items.Select(i => i.Id));
            Assert.Equal("catalog.group.not_empty", (await Assert.ThrowsAsync<BusinessRuleException>(async () =>
                await s.Nomenclature.SetGroupArchivedAsync(merino, true, (await s.Nomenclature.GroupsAsync()).Single(g => g.Id == merino).RowVersion))).Code);

            // Свой справочник «Цвета» и доп. поле номенклатуры типа «Справочник».
            colors = await s.Nomenclature.CreateCatalogAsync("Цвета");
            blue = await s.Nomenclature.AddEntryAsync(colors, "Синий меланж", "0012");
            await s.Nomenclature.AddEntryAsync(colors, "Белый", "0001");
            Assert.Equal("catalog.user.entry_duplicate", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Nomenclature.AddEntryAsync(colors, "Белый", null))).Code);
            colorField = await s.Nomenclature.CreateItemFieldAsync("Цвет", CustomFieldType.Catalog, colors);
            var nm = await s.Nomenclature.CreateItemFieldAsync("Тонина, Nm", CustomFieldType.Number);
            Assert.Equal("custom_field.catalog", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Nomenclature.CreateItemFieldAsync("Без справочника", CustomFieldType.Catalog))).Code);
            Assert.Equal("catalog.user.used", (await Assert.ThrowsAsync<BusinessRuleException>(async () =>
                await s.Nomenclature.SetCatalogArchivedAsync(colors, true, (await s.Nomenclature.CatalogsAsync()).Single().RowVersion))).Code);

            // Карточка: сведения, доп. поля, страна происхождения (код ОКСМ → название).
            var card = await s.Nomenclature.GetItemAsync(item);
            await s.Nomenclature.SaveItemDetailsAsync(item, card.Details with
            {
                Article = "MER-2/48", OriginCountryCode = "156", CustomsDeclaration = "10702010/101026/0012345", TnVedCode = "5107 10 900 0",
                WeightKg = 1m, MinStock = 50m, PurchasePrice = 1_250.5m,
            }, new Dictionary<long, string?> { [colorField] = blue.ToString(), [nm] = "48" }, card.RowVersion);
            card = await s.Nomenclature.GetItemAsync(item);
            Assert.Equal(("MER-2/48", "Китай", "5107109000", 1_250.5m), (card.Details.Article, card.Details.OriginCountryName, card.Details.TnVedCode,
                card.Details.PurchasePrice!.Value));
            Assert.Equal("Пряжа / Шерсть / Меринос", card.GroupPath);
            Assert.Equal(("0012 — Синий меланж", "48"), (card.CustomFields.Single(f => f.FieldId == colorField).Display,
                card.CustomFields.Single(f => f.FieldId == nm).Value));
            Assert.Equal("catalog.item.country", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Nomenclature.SaveItemDetailsAsync(item,
                card.Details with { OriginCountryCode = "15" }, new Dictionary<long, string?>(), card.RowVersion))).Code);
            await Assert.ThrowsAsync<NotFoundException>(() => s.Nomenclature.SaveItemDetailsAsync(item, card.Details,
                new Dictionary<long, string?> { [colorField] = "999999" }, card.RowVersion));
            Assert.Equal([item], (await s.Catalog.ListItemsAsync(new ItemFilter(Search: "MER-2"))).Items.Select(i => i.Id));

            // Штрихкоды: EAN-13 с проверкой контрольной цифры, уникальность в организации, поиск сканером.
            await s.Nomenclature.AddBarcodeAsync(item, "4600000000008");
            Assert.Equal("catalog.barcode.invalid", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Nomenclature.AddBarcodeAsync(item, "4600000000009"))).Code);
            Assert.Equal("catalog.barcode.duplicate", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Nomenclature.AddBarcodeAsync(other, "4600000000008"))).Code);
            Assert.Equal(item, await s.Nomenclature.FindByBarcodeAsync("4600000000008"));
            Assert.Equal([item], (await s.Catalog.ListItemsAsync(new ItemFilter(Search: "4600000000008"))).Items.Select(i => i.Id));

            // Виды цен: основной «Цена продажи» есть у новой организации; цена подставляется в заказ покупателя.
            var types = await s.Nomenclature.PriceTypesAsync();
            Assert.Equal(PriceType.DefaultName, types.Single(t => t.IsDefault).Name);
            priceType = await s.Nomenclature.CreatePriceTypeAsync("Оптовая", true);
            await s.Nomenclature.SetPricesAsync(item, new Dictionary<long, decimal?> { [types.Single().Id] = 2_440m, [priceType] = 2_000m });
            Assert.Equal(2_440m, (await s.Catalog.ListItemsAsync(new ItemFilter(Search: "ПР-1"))).Items.Single().SalePrice);
            var option = (await s.Sales.GetOptionsAsync(Day)).Items.Single(i => i.Id == item);
            Assert.Equal((2_440m, true, "MER-2/48", "4600000000008"), (option.Price!.Value, option.PriceIncludesVat, option.Article, option.Barcodes));
            Assert.Equal(1_250.5m, (await s.Purchases.GetOptionsAsync(Day)).Items.Single(i => i.Id == item).Price);
            Assert.Equal("catalog.price_type.default", (await Assert.ThrowsAsync<BusinessRuleException>(async () =>
                await s.Nomenclature.SetPriceTypeArchivedAsync(types.Single().Id, true, (await s.Nomenclature.PriceTypesAsync()).Single(t => t.IsDefault).RowVersion))).Code);
            await s.Nomenclature.SetPricesAsync(item, new Dictionary<long, decimal?> { [priceType] = null });
            Assert.Null((await s.Nomenclature.GetItemAsync(item)).Prices.Single(p => p.PriceTypeId == priceType).Price);
        }

        // Без права на цены: цены не видны, менять нельзя, закупочная цена в карточке не меняется.
        long senior;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            senior = (await s.Access.InviteAsync(new InviteUserCommand($"senior-{Guid.NewGuid():N}@test.local", "Старший кладовщик",
                SystemRoles.SeniorStorekeeper, null))).UserId;
        }

        await using (var db = host.NewDb())
        {
            (await db.Users.SingleAsync(u => u.Id == senior)).Activate();
            await db.SaveChangesAsync();
        }

        await using (var s = host.As(senior, org.OrganizationId))
        {
            var card = await s.Nomenclature.GetItemAsync(item);
            Assert.Empty(card.Prices);
            Assert.Null(card.Details.PurchasePrice);
            Assert.Null((await s.Catalog.ListItemsAsync(new ItemFilter(Search: "ПР-1"))).Items.Single().SalePrice);
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Nomenclature.SetPricesAsync(item, new Dictionary<long, decimal?> { [priceType] = 1m }));
        }

        var foreign = await CreateOrgAsync();
        await using (var s = host.As(foreign.OwnerUserId, foreign.OrganizationId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => s.Nomenclature.GetItemAsync(item));
            await Assert.ThrowsAsync<NotFoundException>(() => s.Nomenclature.CreateGroupAsync(yarn, "Чужая"));
            await Assert.ThrowsAsync<NotFoundException>(() => s.Nomenclature.AddEntryAsync(colors, "Чужой", null));
            await Assert.ThrowsAsync<NotFoundException>(() => s.Nomenclature.EntriesAsync(colors));
            Assert.Null(await s.Nomenclature.FindByBarcodeAsync("4600000000008"));
            // У другой организации свой основной вид цены и пустые справочники.
            Assert.Single(await s.Nomenclature.PriceTypesAsync());
            Assert.Empty(await s.Nomenclature.CatalogsAsync());
            Assert.Empty(await s.Nomenclature.GroupsAsync());
        }
    }

    private async Task<CreatedOrganization> CreateOrgAsync()
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
