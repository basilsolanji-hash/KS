using KnitErp.Application.Access;
using KnitErp.Application.Catalog;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Application.Warehousing;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Warehousing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>Начальные остатки: черновик → утверждение другим человеком → движения регистра и остатки.</summary>
public sealed class OpeningBalanceFlowTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private static int _innSeed = 700_000_000;
    private static readonly DateOnly AsOf = new(2026, 10, 1);

    [SqlFact]
    public async Task Senior_storekeeper_enters_owner_approves_and_balances_appear()
    {
        var f = await SetUpAsync();
        long doc;
        await using (var s = host.As(f.Senior, f.Org.OrganizationId))
        {
            doc = await s.OpeningBalances.CreateAsync(f.Yarn, AsOf, "Перенос из старого учёта");
            var card = await s.OpeningBalances.GetAsync(doc);
            Assert.Equal("НО-000001", card.Number);
            await s.OpeningBalances.SetLineAsync(doc, f.Wool, 120.5m, card.RowVersion);
            card = await s.OpeningBalances.GetAsync(doc);
            Assert.Equal("stock.quantity.precision", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.OpeningBalances.SetLineAsync(doc, f.Buttons, 1.5m, card.RowVersion))).Code);
            await s.OpeningBalances.SetLineAsync(doc, f.Buttons, 300m, card.RowVersion);
            card = await s.OpeningBalances.GetAsync(doc);
            await s.OpeningBalances.SubmitAsync(doc, card.RowVersion);

            card = await s.OpeningBalances.GetAsync(doc);
            Assert.False(card.CanEdit);
            Assert.False(card.CanApprove);
            var denied = await Assert.ThrowsAsync<AccessDeniedException>(() => s.OpeningBalances.ApproveAsync(doc, card.RowVersion));
            Assert.Equal(Permissions.OpeningBalanceApprove, denied.PermissionCode);
        }

        await using (var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId))
        {
            var card = await s.OpeningBalances.GetAsync(doc);
            Assert.True(card.CanApprove);
            await s.OpeningBalances.ApproveAsync(doc, card.RowVersion);
            card = await s.OpeningBalances.GetAsync(doc);
            Assert.Equal((OpeningBalanceStatus.Approved, "Владелец " + f.Inn), (card.Status, card.ApprovedBy));

            var balances = await s.Stock.BalancesAsync(new StockFilter());
            Assert.Equal(2, balances.Count);
            Assert.Equal(120.5m, balances.Single(b => b.ItemId == f.Wool).Quantity);
        }

        await using var db = host.NewDb();
        Assert.Equal(2, await db.StockMovements.CountAsync(m => m.Source == StockSource.OpeningBalance && m.SourceId == doc));
        Assert.True(await db.AuditEntries.AnyAsync(e => e.Action == AuditActions.StockDocumentApproved && e.EntityId == doc.ToString()));
    }

    [SqlFact]
    public async Task Author_with_approve_right_still_cannot_approve_own_document()
    {
        var f = await SetUpAsync();
        await using (var db = host.NewDb())
        {
            db.RoleAssignments.Add(RoleAssignment.ForPermission(f.Org.OrganizationId, f.Senior, Permissions.OpeningBalanceApprove, false,
                f.Org.OwnerUserId, host.Clock.UtcNow, "Тест четырёх глаз"));
            await db.SaveChangesAsync();
        }

        await using var s = host.As(f.Senior, f.Org.OrganizationId);
        var doc = await s.OpeningBalances.CreateAsync(f.Yarn, AsOf, null);
        await s.OpeningBalances.SetLineAsync(doc, f.Wool, 1m, (await s.OpeningBalances.GetAsync(doc)).RowVersion);
        await s.OpeningBalances.SubmitAsync(doc, (await s.OpeningBalances.GetAsync(doc)).RowVersion);
        var card = await s.OpeningBalances.GetAsync(doc);
        Assert.False(card.CanApprove);
        Assert.Equal("stock.opening.self_approval",
            (await Assert.ThrowsAsync<BusinessRuleException>(() => s.OpeningBalances.ApproveAsync(doc, card.RowVersion))).Code);
    }

    [SqlFact]
    public async Task Item_opening_balance_is_entered_once_per_warehouse_and_return_needs_reason()
    {
        var f = await SetUpAsync();
        var first = await SubmittedAsync(f, f.Yarn, (f.Wool, 10m));
        await ApproveAsync(f, first);

        var second = await SubmittedAsync(f, f.Yarn, (f.Wool, 5m), (f.Buttons, 1m));
        await using (var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId))
        {
            var card = await s.OpeningBalances.GetAsync(second);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => s.OpeningBalances.ApproveAsync(second, card.RowVersion));
            Assert.Equal("stock.opening.already_entered", ex.Code);
            Assert.Contains("ПР-1", ex.Message);

            await Assert.ThrowsAsync<BusinessRuleException>(() => s.OpeningBalances.ReturnAsync(second, "", card.RowVersion));
            await s.OpeningBalances.ReturnAsync(second, "Шерсть уже введена в НО-000001", card.RowVersion);
        }

        await using (var s = host.As(f.Senior, f.Org.OrganizationId))
        {
            var card = await s.OpeningBalances.GetAsync(second);
            Assert.Equal((OpeningBalanceStatus.Draft, "Шерсть уже введена в НО-000001"), (card.Status, card.ReturnReason));
            await s.OpeningBalances.RemoveLineAsync(second, f.Wool, card.RowVersion);
            await s.OpeningBalances.SubmitAsync(second, (await s.OpeningBalances.GetAsync(second)).RowVersion);
        }

        await ApproveAsync(f, second);
    }

    [SqlFact]
    public async Task Warehouse_scope_limits_documents_and_balances()
    {
        var f = await SetUpAsync();
        long other;
        await using (var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId))
        {
            other = await s.Warehouses.CreateWarehouseAsync("Склад готовой продукции", null);
        }

        await using (var s = host.As(f.Senior, f.Org.OrganizationId))
        {
            Assert.Equal([f.Yarn], (await s.OpeningBalances.ListWarehousesForEntryAsync()).Select(w => w.Id));
            await Assert.ThrowsAsync<NotFoundException>(() => s.OpeningBalances.CreateAsync(other, AsOf, null));
        }

        await ApproveAsync(f, await SubmittedAsync(f, f.Yarn, (f.Wool, 7m)));

        // Второй старший кладовщик без области склада заводит документ по другому складу.
        var seniorAll = await InviteActiveAsync(f.Org, SystemRoles.SeniorStorekeeper, null);
        long foreignDoc;
        await using (var s = host.As(seniorAll, f.Org.OrganizationId))
        {
            foreignDoc = await s.OpeningBalances.CreateAsync(other, AsOf, null);
        }

        await using (var s = host.As(f.Senior, f.Org.OrganizationId))
        {
            Assert.DoesNotContain(await s.OpeningBalances.ListAsync(), d => d.Id == foreignDoc);
            await Assert.ThrowsAsync<NotFoundException>(() => s.OpeningBalances.GetAsync(foreignDoc));
        }

        var keeper = await InviteActiveAsync(f.Org, SystemRoles.Storekeeper, f.Yarn);
        await using (var s = host.As(keeper, f.Org.OrganizationId))
        {
            var balances = await s.Stock.BalancesAsync(new StockFilter());
            Assert.All(balances, b => Assert.Equal(f.Yarn, b.WarehouseId));
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.OpeningBalances.CreateAsync(f.Yarn, AsOf, null));
        }
    }

    [SqlFact]
    public async Task Lines_import_is_all_or_nothing()
    {
        var f = await SetUpAsync();
        long doc;
        await using (var s = host.As(f.Senior, f.Org.OrganizationId))
        {
            doc = await s.OpeningBalances.CreateAsync(f.Yarn, AsOf, null);
            var bad = File(["пр-1", "12,5"], ["ПР-1", "3"], ["НЕТ-1", "1"], ["Ф-1", "1,5"], ["Ф-1", "abc"]);
            var result = await s.OpeningBalances.ImportLinesAsync(doc, bad, (await s.OpeningBalances.GetAsync(doc)).RowVersion);
            Assert.False(result.Applied);
            Assert.Equal(4, result.ErrorRows);
            Assert.Empty((await s.OpeningBalances.GetAsync(doc)).Lines);

            var good = File(["пр-1", "1 250,5"], ["Ф-1", "40"]);
            result = await s.OpeningBalances.ImportLinesAsync(doc, good, (await s.OpeningBalances.GetAsync(doc)).RowVersion);
            Assert.True(result.Applied);
            var lines = (await s.OpeningBalances.GetAsync(doc)).Lines;
            Assert.Equal(1250.5m, lines.Single(l => l.Code == "ПР-1").Quantity);
        }
    }

    [SqlFact]
    public async Task Movements_cannot_be_changed_and_numbers_are_unique_under_concurrency()
    {
        var f = await SetUpAsync();
        await ApproveAsync(f, await SubmittedAsync(f, f.Yarn, (f.Wool, 2m)));

        await using (var db = host.NewDb())
        {
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [kniterp].[stock_movements] SET [Quantity] = 1000 WHERE [OrganizationId] = {f.Org.OrganizationId}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [kniterp].[stock_movements] WHERE [OrganizationId] = {f.Org.OrganizationId}"));
        }

        var created = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var s = host.As(f.Senior, f.Org.OrganizationId);
            return await s.OpeningBalances.CreateAsync(f.Yarn, AsOf, null);
        }));
        await using var check = host.NewDb();
        var numbers = await check.OpeningBalances.Where(d => created.Contains(d.Id)).Select(d => d.Number).ToListAsync();
        Assert.Equal(6, numbers.Distinct().Count());
    }

    private sealed record Fixture(CreatedOrganization Org, string Inn, long Yarn, long Wool, long Buttons, long Senior);

    private async Task<Fixture> SetUpAsync()
    {
        var inn = NextValidInn();
        CreatedOrganization org;
        await using (var s = host.As(null, null))
        {
            org = await s.Organizations.CreateWithOwnerAsync(new CreateOrganizationCommand(
                $"Тестовая организация {inn}", $"Тест {inn}", inn, null, false, "Europe/Moscow", $"owner-{inn}@test.local", $"Владелец {inn}"));
        }

        long yarn, wool, buttons;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var units = await s.Catalog.ListUnitsAsync();
            yarn = await s.Warehouses.CreateWarehouseAsync("Склад пряжи", null);
            wool = await s.Catalog.CreateItemAsync(new ItemCommand("ПР-1", "Пряжа шерсть", ItemType.RawMaterial, units.Single(u => u.Symbol == "кг").Id, null));
            buttons = await s.Catalog.CreateItemAsync(new ItemCommand("Ф-1", "Пуговица", ItemType.Accessory, units.Single(u => u.Symbol == "шт").Id, null));
        }

        var senior = await InviteActiveAsync(org, SystemRoles.SeniorStorekeeper, yarn);
        return new Fixture(org, inn, yarn, wool, buttons, senior);
    }

    private async Task<long> SubmittedAsync(Fixture f, long warehouse, params (long Item, decimal Qty)[] lines)
    {
        await using var s = host.As(f.Senior, f.Org.OrganizationId);
        var doc = await s.OpeningBalances.CreateAsync(warehouse, AsOf, null);
        foreach (var (item, qty) in lines)
        {
            await s.OpeningBalances.SetLineAsync(doc, item, qty, (await s.OpeningBalances.GetAsync(doc)).RowVersion);
        }

        await s.OpeningBalances.SubmitAsync(doc, (await s.OpeningBalances.GetAsync(doc)).RowVersion);
        return doc;
    }

    private async Task ApproveAsync(Fixture f, long doc)
    {
        await using var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId);
        await s.OpeningBalances.ApproveAsync(doc, (await s.OpeningBalances.GetAsync(doc)).RowVersion);
    }

    private static MemoryStream File(params string[][] rows) =>
        new(SqlTestHost.Spreadsheet.Write([new SheetData("Остатки", OpeningBalanceService.ImportColumns, rows)]));

    private async Task<long> InviteActiveAsync(CreatedOrganization org, string roleCode, long? warehouseId)
    {
        long id;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            id = (await s.Access.InviteAsync(new InviteUserCommand($"{roleCode}-{Guid.NewGuid():N}@test.local",
                SystemRoles.NameOf(roleCode), roleCode, null, WarehouseId: warehouseId))).UserId;
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
