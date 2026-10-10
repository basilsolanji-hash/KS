using KnitErp.Application.Access;
using KnitErp.Application.Catalog;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Warehousing;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>Контрагенты, причины операций, шаблонный импорт и выгрузка номенклатуры.</summary>
public sealed class ReferenceDataTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private static int _innSeed = 600_000_000;

    [SqlFact]
    public async Task Counterparty_inn_kpp_pair_is_unique_among_active()
    {
        var org = await CreateOrganizationAsync();
        await using var s = host.As(org.OwnerUserId, org.OrganizationId);

        var head = await s.Counterparties.CreateAsync(new CounterpartyCommand("ООО «Пряжа»", "7707083893", "773601001", true, false, null));
        await s.Counterparties.CreateAsync(new CounterpartyCommand("ООО «Пряжа», филиал", "7707083893", "500301001", true, false, null));
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Counterparties.CreateAsync(new CounterpartyCommand("Дубль", "7707083893", "773601001", false, true, null)));
        Assert.Equal("catalog.counterparty.duplicate", ex.Code);

        var row = (await s.Counterparties.ListAsync(new CounterpartyFilter())).Counterparties.Single(c => c.Id == head);
        await s.Counterparties.ArchiveAsync(head, row.RowVersion);
        await s.Counterparties.CreateAsync(new CounterpartyCommand("ООО «Пряжа» (новая карточка)", "7707083893", "773601001", true, false, null));

        var archived = (await s.Counterparties.ListAsync(new CounterpartyFilter(IncludeArchived: true))).Counterparties.Single(c => c.Id == head);
        Assert.Equal("catalog.counterparty.duplicate",
            (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Counterparties.RestoreAsync(head, archived.RowVersion))).Code);

        Assert.Equal(2, (await s.Counterparties.ListAsync(new CounterpartyFilter(Suppliers: true))).Counterparties.Count);
        Assert.Empty((await s.Counterparties.ListAsync(new CounterpartyFilter(Customers: true))).Counterparties);
    }

    [SqlFact]
    public async Task New_organization_has_default_reasons_and_storekeeper_cannot_edit_them()
    {
        var org = await CreateOrganizationAsync();
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var list = await s.Reasons.ListAsync();
            Assert.Equal(OperationReason.Defaults.Count, list.Reasons.Count);
            Assert.True(list.Reasons.Single(r => r.Name == "Брак").RequiresComment);
            await s.Reasons.CreateAsync(StockOperationKind.WriteOff, "Образцы для выставки", requiresComment: true);
            Assert.Equal("warehouse.reason.duplicate", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Reasons.CreateAsync(StockOperationKind.WriteOff, "Брак", false))).Code);
            await s.Reasons.CreateAsync(StockOperationKind.Inventory, "Брак", false);
        }

        var keeper = await InviteActiveAsync(org, SystemRoles.Storekeeper);
        await using (var s = host.As(keeper, org.OrganizationId))
        {
            Assert.False((await s.Reasons.ListAsync()).CanEdit);
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Reasons.CreateAsync(StockOperationKind.WriteOff, "Своя причина", false));
        }
    }

    [SqlFact]
    public async Task Import_shows_protocol_and_applies_nothing_while_errors_remain()
    {
        var org = await CreateOrganizationAsync();
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var kg = (await s.Catalog.ListUnitsAsync()).Single(u => u.Symbol == "кг").Id;
            var archivedId = await s.Catalog.CreateItemAsync(new ItemCommand("OLD-1", "Старая пряжа", ItemType.RawMaterial, kg, null));
            var row = (await s.Catalog.ListItemsAsync(new ItemFilter())).Items.Single();
            await s.Catalog.ArchiveItemAsync(archivedId, row.RowVersion);
        }

        var file = Workbook(
            ["ПР-1", "Пряжа хлопок", "Пряжа и сырьё", "кг", ""],
            ["ПР-2", "Пряжа шерсть", "пряжа и сырьё", "Килограмм", "серая"],
            ["пр-1", "Повтор кода", "Пряжа и сырьё", "кг", ""],
            ["Ф-1", "Пуговица", "Фурнитура", "бобина", ""],
            ["Ф-2", "Кнопка", "Металл", "шт", ""],
            ["OLD-1", "Архивный код", "Пряжа и сырьё", "кг", ""],
            ["", "", "", "", ""]);

        ItemImportPlan plan;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            plan = await s.ItemExchange.PreviewAsync(file);
        }

        Assert.Equal(6, plan.Total);
        Assert.Equal(1, plan.ToCreate);
        Assert.Equal(5, plan.ErrorRows);
        Assert.False(plan.CanApply);
        Assert.Contains("повторяется в строках 2, 4", plan.Rows.Single(r => r.RowNumber == 2).Errors.Single());
        Assert.Contains("Неизвестная единица «бобина»", plan.Rows.Single(r => r.RowNumber == 5).Errors.Single());
        Assert.Contains("Неизвестный тип «Металл»", plan.Rows.Single(r => r.RowNumber == 6).Errors.Single());
        Assert.Contains("архивной позиции", plan.Rows.Single(r => r.RowNumber == 7).Errors.Single());

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => s.ItemExchange.ApplyAsync(plan.Rows));
            Assert.Equal("import.has_errors", ex.Code);
            Assert.Empty((await s.Catalog.ListItemsAsync(new ItemFilter())).Items);
        }
    }

    [SqlFact]
    public async Task Clean_import_creates_and_updates_then_export_round_trips()
    {
        var org = await CreateOrganizationAsync();
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var pcs = (await s.Catalog.ListUnitsAsync()).Single(u => u.Symbol == "шт").Id;
            await s.Catalog.CreateItemAsync(new ItemCommand("ГП-1", "Свитер", ItemType.Finished, pcs, null));
            await s.Catalog.CreateItemAsync(new ItemCommand("ГП-2", "Жилет", ItemType.Finished, pcs, null));
        }

        var file = Workbook(
            ["ПР-1", "Пряжа хлопок", "Пряжа и сырьё", "кг", ""],
            ["0042", "Нитки", "Материалы", "166", ""],
            ["ГП-1", "Свитер женский", "Готовая продукция", "шт", ""],
            ["ГП-2", "Жилет", "Готовая продукция", "шт", ""]);

        ItemImportPlan plan;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            plan = await s.ItemExchange.PreviewAsync(file);
            Assert.Equal((2, 1, 1, 0), (plan.ToCreate, plan.ToUpdate, plan.Unchanged, plan.ErrorRows));
            Assert.True(plan.CanApply);
            var result = await s.ItemExchange.ApplyAsync(plan.Rows);
            Assert.Equal((2, 1), (result.Created, result.Updated));
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var items = (await s.Catalog.ListItemsAsync(new ItemFilter())).Items;
            Assert.Equal(4, items.Count);
            Assert.Equal("кг", items.Single(i => i.Code == "0042").UnitSymbol);
            Assert.Equal("Свитер женский", items.Single(i => i.Code == "ГП-1").Name);

            // Выгрузка читается обратно тем же импортом и ничего не меняет — формат один.
            var export = await s.ItemExchange.ExportAsync();
            var again = await s.ItemExchange.PreviewAsync(new MemoryStream(export));
            Assert.Equal((4, 0, 0, 4), (again.Total, again.ToCreate, again.ToUpdate, again.Unchanged));
            Assert.False(again.CanApply);
        }

        await using var db = host.NewDb();
        var summary = await db.AuditEntries.SingleAsync(e => e.OrganizationId == org.OrganizationId && e.Action == AuditActions.CatalogImported);
        Assert.Equal("строк 4: создано 2, обновлено 1, без изменений 1", summary.After);
    }

    [SqlFact]
    public async Task Wrong_header_and_rights_are_checked()
    {
        var org = await CreateOrganizationAsync();
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var wrong = new MemoryStream(SqlTestHost.Spreadsheet.Write([new SheetData("Лист", ["Артикул", "Название"], [["1", "2"]])]));
            Assert.Equal("import.header", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.ItemExchange.PreviewAsync(wrong))).Code);
            Assert.Equal("import.file_invalid", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.ItemExchange.PreviewAsync(new MemoryStream("не excel"u8.ToArray())))).Code);
            Assert.NotEmpty(await s.ItemExchange.TemplateAsync());
        }

        var senior = await InviteActiveAsync(org, SystemRoles.SeniorStorekeeper);
        await using (var s = host.As(senior, org.OrganizationId))
        {
            var ex = await Assert.ThrowsAsync<AccessDeniedException>(() => s.ItemExchange.PreviewAsync(Workbook(["A", "B", "Прочее", "шт", ""])));
            Assert.Equal(Permissions.CatalogImport, ex.PermissionCode);
            Assert.NotEmpty(await s.ItemExchange.ExportAsync());
        }
    }

    private static MemoryStream Workbook(params string[][] rows) =>
        new(SqlTestHost.Spreadsheet.Write([new SheetData("Номенклатура", ItemExchangeService.Columns, rows)]));

    private async Task<CreatedOrganization> CreateOrganizationAsync()
    {
        var inn = NextValidInn();
        await using var s = host.As(null, null);
        return await s.Organizations.CreateWithOwnerAsync(new CreateOrganizationCommand(
            $"Тестовая организация {inn}", $"Тест {inn}", inn, null, false, "Europe/Moscow",
            $"owner-{inn}@test.local", $"Владелец {inn}"));
    }

    private async Task<long> InviteActiveAsync(CreatedOrganization org, string roleCode)
    {
        long id;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            id = (await s.Access.InviteAsync(new InviteUserCommand($"{roleCode}-{Guid.NewGuid():N}@test.local",
                SystemRoles.NameOf(roleCode), roleCode, SystemRoles.IsAdministrative(roleCode) ? "Тест" : null))).UserId;
        }

        await using var db = host.NewDb();
        (await db.Users.SingleAsync(u => u.Id == id)).Activate();
        await db.SaveChangesAsync();
        return id;
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
