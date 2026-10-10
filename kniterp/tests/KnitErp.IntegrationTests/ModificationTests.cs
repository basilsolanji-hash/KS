using KnitErp.Application.Access;
using KnitErp.Application.Catalog;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Application.Warehousing;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Warehousing;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>Номенклатура, часть 2 (D82): модификации по характеристикам, фото позиции, Excel со всеми полями карточки.</summary>
public sealed class ModificationTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private static int _innSeed = 840_000_000;
    private static readonly DateOnly Day = new(2026, 10, 9);

    // Минимальные заголовки файлов: сервис проверяет тип по содержимому.
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46];

    [SqlFact]
    public async Task Modifications_from_matrix_have_own_codes_prices_and_stock()
    {
        var org = await CreateOrgAsync();
        await using var s = host.As(org.OwnerUserId, org.OrganizationId);
        var unit = (await s.Catalog.ListUnitsAsync()).Single(u => u.Symbol == "шт").Id;
        var sweater = await s.Catalog.CreateItemAsync(new ItemCommand("СВ-1", "Свитер", ItemType.Finished, unit, null));
        var price = (await s.Nomenclature.PriceTypesAsync()).Single(t => t.IsDefault).Id;
        await s.Nomenclature.SetPricesAsync(sweater, new Dictionary<long, decimal?> { [price] = 2500m });

        // Характеристики — справочник организации; повтор названия — ошибка.
        var color = await s.Modifications.CreateCharacteristicAsync("Цвет");
        var size = await s.Modifications.CreateCharacteristicAsync("Размер");
        Assert.Equal("catalog.characteristic.duplicate", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Modifications.CreateCharacteristicAsync("цвет"))).Code);
        Assert.Equal("catalog.variant.no_values", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Modifications.CreateModificationsAsync(sweater, new Dictionary<long, string?> { [color] = " " }))).Code);

        // 2 цвета × 2 размера = 4 модификации; повтор — пропускается.
        var result = await s.Modifications.CreateModificationsAsync(sweater, new Dictionary<long, string?> { [color] = "красный, синий", [size] = "46; 48" });
        Assert.Equal((4, 0), (result.Created, result.Skipped));
        result = await s.Modifications.CreateModificationsAsync(sweater, new Dictionary<long, string?> { [color] = "Красный", [size] = "46, 50" });
        Assert.Equal((1, 1), (result.Created, result.Skipped));

        var variants = await s.Modifications.VariantsAsync(sweater);
        Assert.Equal(5, variants.Modifications.Count);
        var red46 = variants.Modifications.Single(m => m.Name == "Свитер, красный, 46");
        Assert.StartsWith("СВ-1-", red46.Code);
        Assert.Equal(["Цвет: красный", "Размер: 46"], red46.Values.Select(v => $"{v.Characteristic}: {v.Value}"));

        // Модификация — отдельная позиция: цена скопирована, у неё своя карточка и свой остаток; своих модификаций у неё нет.
        Assert.Equal(2500m, (await s.Nomenclature.GetItemAsync(red46.Id)).Prices.Single(p => p.IsDefault).Price);
        Assert.Equal("catalog.variant.nested", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Modifications.CreateModificationsAsync(red46.Id, new Dictionary<long, string?> { [size] = "52" }))).Code);
        var own = await s.Modifications.VariantsAsync(red46.Id);
        Assert.Equal(sweater, own.Parent!.Id);

        var store = await s.Warehouses.CreateWarehouseAsync("Склад готовой продукции", null);
        var reason = await s.Db.OperationReasons.Where(r => r.OrganizationId == org.OrganizationId && r.Kind == StockOperationKind.Receipt)
            .Select(r => (long?)r.Id).FirstAsync();
        var doc = await s.Documents.CreateAsync(StockOperationKind.Receipt, new StockDocumentHeader(store, null, null, reason, Day, null));
        await s.Documents.SetLineAsync(doc, red46.Id, 7, (await s.Documents.GetAsync(doc)).RowVersion);
        await s.Documents.PostAsync(doc, (await s.Documents.GetAsync(doc)).RowVersion);
        Assert.Equal(7m, (await s.Modifications.VariantsAsync(sweater)).Modifications.Single(m => m.Id == red46.Id).Stock);
        Assert.Equal(0m, (await s.Nomenclature.GetItemAsync(sweater)).Stock);

        // Смена значений: сочетание другой модификации — отказ; новое — сохраняется.
        var blue48 = variants.Modifications.Single(m => m.Name == "Свитер, синий, 48");
        Assert.Equal("catalog.variant.duplicate", (await Assert.ThrowsAsync<BusinessRuleException>(async () =>
            await s.Modifications.SetValuesAsync(blue48.Id, new Dictionary<long, string?> { [color] = "КРАСНЫЙ", [size] = "46" },
                (await s.Modifications.VariantsAsync(blue48.Id)).RowVersion))).Code);
        await s.Modifications.SetValuesAsync(blue48.Id, new Dictionary<long, string?> { [color] = "синий", [size] = "52" },
            (await s.Modifications.VariantsAsync(blue48.Id)).RowVersion);
        Assert.Equal("52", (await s.Modifications.VariantsAsync(blue48.Id)).Values.Single(v => v.CharacteristicId == size).Value);

        // Список: у основной — число модификаций, у модификаций — ссылка на основную.
        var list = (await s.Catalog.ListItemsAsync(new ItemFilter())).Items;
        Assert.Equal(5, list.Single(i => i.Id == sweater).Modifications);
        Assert.Equal(5, list.Count(i => i.ParentItemId == sweater));

        // В БД набор характеристик уникален у одной основной позиции.
        await using var db = host.NewDb();
        var copy = await db.Items.SingleAsync(i => i.Id == red46.Id);
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(async () =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE kniterp.items SET VariantKey = (SELECT VariantKey FROM kniterp.items WHERE Id = {0}) WHERE Id = {1}", copy.Id, blue48.Id);
        });
    }

    [SqlFact]
    public async Task Photos_are_checked_by_content_ordered_and_scoped_to_organization()
    {
        var org = await CreateOrgAsync();
        long item, first, second;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var unit = (await s.Catalog.ListUnitsAsync()).Single(u => u.Symbol == "шт").Id;
            item = await s.Catalog.CreateItemAsync(new ItemCommand("ПУ-1", "Пуговица", ItemType.Accessory, unit, null));
            Assert.Equal("catalog.photo.type", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Modifications.AddPhotoAsync(item, "<svg onload=alert(1)>"u8.ToArray()))).Code);
            Assert.Equal("catalog.photo.too_large", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Modifications.AddPhotoAsync(item, [.. Jpeg, .. new byte[ItemPhoto.MaxBytes]]))).Code);

            first = await s.Modifications.AddPhotoAsync(item, Png);
            second = await s.Modifications.AddPhotoAsync(item, Jpeg);
            Assert.Equal([first, second], (await s.Modifications.PhotosAsync(item)).Select(p => p.Id));
            await s.Modifications.MakeMainPhotoAsync(second);
            Assert.Equal([second, first], (await s.Modifications.PhotosAsync(item)).Select(p => p.Id));
            Assert.Equal(second, (await s.Catalog.ListItemsAsync(new ItemFilter())).Items.Single(i => i.Id == item).PhotoId);
            var (type, content) = await s.Modifications.GetPhotoAsync(first);
            Assert.Equal(("image/png", Png.Length), (type, content.Length));

            for (var i = 2; i < ItemPhoto.MaxPerItem; i++)
            {
                await s.Modifications.AddPhotoAsync(item, Jpeg);
            }

            Assert.Equal("catalog.photo.too_many", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Modifications.AddPhotoAsync(item, Jpeg))).Code);
            await s.Modifications.RemovePhotoAsync(first);
            Assert.Equal(ItemPhoto.MaxPerItem - 1, (await s.Modifications.PhotosAsync(item)).Count);
        }

        // Чужая организация не видит и не удаляет фото.
        var other = await CreateOrgAsync();
        await using (var s = host.As(other.OwnerUserId, other.OrganizationId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => s.Modifications.GetPhotoAsync(second));
            await Assert.ThrowsAsync<NotFoundException>(() => s.Modifications.RemovePhotoAsync(second));
            Assert.Empty(await s.Modifications.PhotosAsync(item));
        }
    }

    [SqlFact]
    public async Task Excel_round_trip_with_card_fields_prices_barcodes_and_modifications()
    {
        var org = await CreateOrgAsync();
        await using var s = host.As(org.OwnerUserId, org.OrganizationId);
        var unit = (await s.Catalog.ListUnitsAsync()).Single(u => u.Symbol == "шт").Id;
        var yarn = await s.Nomenclature.CreateGroupAsync(null, "Изделия");
        await s.Nomenclature.CreateGroupAsync(yarn, "Свитеры");
        await s.Modifications.CreateCharacteristicAsync("Цвет");
        await s.Modifications.CreateCharacteristicAsync("Размер");
        var sweater = await s.Catalog.CreateItemAsync(new ItemCommand("СВ-1", "Свитер", ItemType.Finished, unit, null));

        // Выгрузка → загрузка без правок: всё «без изменений».
        var exported = await s.ItemExchange.ExportAsync();
        var plan = await s.ItemExchange.PreviewAsync(new MemoryStream(exported));
        Assert.Equal((1, 0), (plan.Unchanged, plan.ErrorRows));

        // Новые поля, цена, штрихкод, группа и модификация из этого же файла.
        var header = ItemExchangeService.Columns.Concat([ItemExchangeService.ArticleColumn, ItemExchangeService.GroupColumn,
            ItemExchangeService.BarcodesColumn, ItemExchangeService.CountryColumn, ItemExchangeService.WeightColumn, ItemExchangeService.ParentColumn,
            ItemExchangeService.VariantColumn, ItemExchangeService.PricePrefix + "Цена продажи"]).ToList();
        string[] Row(params string[] v) => v;
        plan = await s.ItemExchange.PreviewAsync(Workbook(header,
            Row("СВ-1", "Свитер", "Готовая продукция", "шт", "", "SW-01", "Изделия / Свитеры", "4600000000008", "643", "0,45", "", "", "2 500,00"),
            Row("СВ-1-К48", "Свитер, красный, 48", "Готовая продукция", "шт", "", "SW-01", "Изделия / Свитеры", "", "643", "0,45", "СВ-1",
                "Цвет: красный; Размер: 48", "2600"),
            Row("ШП-1", "Шапка", "Готовая продукция", "шт", "", "", "", "", "", "", "", "", "")));
        Assert.Equal((0, 2, 1), (plan.ErrorRows, plan.ToCreate, plan.ToUpdate));
        Assert.Equal((2, 1), (await s.ItemExchange.ApplyAsync(plan.Rows) is var applied ? (applied.Created, applied.Updated) : default));

        var card = await s.Nomenclature.GetItemAsync(sweater);
        Assert.Equal(("SW-01", "Изделия / Свитеры", "Россия", 0.45m), (card.Details.Article, card.GroupPath, card.Details.OriginCountryName, card.Details.WeightKg));
        Assert.Equal("4600000000008", card.Barcodes.Single().Code);
        Assert.Equal(2500m, card.Prices.Single(p => p.IsDefault).Price);
        var red = (await s.Modifications.VariantsAsync(sweater)).Modifications.Single();
        Assert.Equal(("СВ-1-К48", "Цвет: красный, Размер: 48"), (red.Code, string.Join(", ", red.Values.Select(v => $"{v.Characteristic}: {v.Value}"))));
        Assert.Equal(2600m, (await s.Nomenclature.GetItemAsync(red.Id)).Prices.Single(p => p.IsDefault).Price);

        // Повторная загрузка той же выгрузки — без изменений, в том числе для модификации.
        plan = await s.ItemExchange.PreviewAsync(new MemoryStream(await s.ItemExchange.ExportAsync()));
        Assert.Equal((3, 0, 0), (plan.Unchanged, plan.ToUpdate, plan.ErrorRows));

        // Ошибки: неизвестный столбец, группа, характеристика, чужой штрихкод, повтор набора характеристик.
        var unknown = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.ItemExchange.PreviewAsync(Workbook([.. ItemExchangeService.Columns, "Цвет изделия"], Row("X-1", "X", "Готовая продукция", "шт", "", "красный"))));
        Assert.Equal("import.header", unknown.Code);
        plan = await s.ItemExchange.PreviewAsync(Workbook(header,
            Row("Х-1", "Шарф", "Готовая продукция", "шт", "", "", "Нет такой", "4600000000008", "", "", "", "", ""),
            Row("СВ-1-К48Б", "Свитер, красный, 48", "Готовая продукция", "шт", "", "", "", "", "", "", "СВ-1", "Цвет: красный; Размер: 48", ""),
            Row("СВ-1-Р", "Свитер", "Готовая продукция", "шт", "", "", "", "", "", "", "СВ-1", "Рост: 170", "")));
        Assert.Equal(3, plan.ErrorRows);
        var errors = string.Join(" | ", plan.Rows.SelectMany(r => r.Errors));
        Assert.Contains("Группа «Нет такой» не найдена", errors);
        Assert.Contains("Штрихкод 4600000000008 уже у позиции СВ-1", errors);
        Assert.Contains("уже есть модификация с такими характеристиками", errors);
        Assert.Contains("Характеристики «Рост» нет", errors);
    }

    private static MemoryStream Workbook(IReadOnlyList<string> columns, params IReadOnlyList<string>[] rows) =>
        new(SqlTestHost.Spreadsheet.Write([new SheetData("Лист", columns, rows)]));

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
