using KnitErp.Application.Access;
using KnitErp.Application.Catalog;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Application.Warehousing;
using KnitErp.Domain.Access;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Warehousing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>Инвентаризация, закрытый период и отчёты «Движения» и «Обороты».</summary>
public sealed class InventoryAndReportsTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private static int _innSeed = 720_000_000;
    private static readonly DateOnly Sep10 = new(2026, 9, 10);
    private static readonly DateOnly Sep20 = new(2026, 9, 20);
    private static readonly DateOnly Oct5 = new(2026, 10, 5);

    [SqlFact]
    public async Task Inventory_writes_surplus_and_shortage_and_shows_in_reports()
    {
        var f = await SetUpAsync();
        await PostedAsync(f, StockOperationKind.Receipt, Sep10, f.Yarn, null, (f.Wool, 50m), (f.Buttons, 100m));
        await PostedAsync(f, StockOperationKind.Transfer, Sep20, f.Yarn, f.Shop, (f.Wool, 20m));

        long inv;
        await using (var s = host.As(f.Senior, f.Org.OrganizationId))
        {
            inv = await s.Inventory.CreateAsync(f.Yarn, Oct5, null);
            var card = await s.Inventory.GetAsync(inv);
            Assert.Equal("ИН-000001", card.Number);
            Assert.Equal([(f.Wool, 30m), (f.Buttons, 100m)], card.Lines.Select(l => (l.ItemId, l.Book)).OrderByDescending(x => x.Book == 30m));
            Assert.Equal(2, card.NotCounted);

            var sheet = SqlTestHost.Spreadsheet.Write([new SheetData("Факт", OpeningBalanceService.ImportColumns, [["ПР-1", "28,5"], ["Ф-1", "0"]])]);
            var result = await s.Inventory.ImportCountsAsync(inv, new MemoryStream(sheet), card.RowVersion);
            Assert.True(result.Applied);
            card = await s.Inventory.GetAsync(inv);
            await s.Inventory.SetCountedAsync(inv, f.Buttons, 104m, card.RowVersion);

            card = await s.Inventory.GetAsync(inv);
            Assert.Equal("stock.inventory.comment_required",
                (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Inventory.PostAsync(inv, card.RowVersion))).Code);
        }

        await using (var s = host.As(f.Senior, f.Org.OrganizationId))
        {
            var card = await s.Inventory.GetAsync(inv);
            await s.Inventory.UpdateHeaderAsync(inv, Oct5, "Недостача пряжи — усушка", card.RowVersion);
            await s.Inventory.PostAsync(inv, (await s.Inventory.GetAsync(inv)).RowVersion);
            card = await s.Inventory.GetAsync(inv);
            Assert.Equal(InventoryStatus.Posted, card.Status);
            Assert.Equal([-1.5m, 4m], card.Deviations.Select(d => d.Difference).Order());
            var row = (await s.Inventory.ListAsync()).Single(r => r.Id == inv);
            Assert.Equal((1, 1), (row.SurplusCount, row.ShortageCount));
        }

        await using (var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId))
        {
            var balances = await s.Stock.BalancesAsync(new StockFilter(f.Yarn));
            Assert.Equal(28.5m, balances.Single(b => b.ItemId == f.Wool).Quantity);
            Assert.Equal(104m, balances.Single(b => b.ItemId == f.Buttons).Quantity);

            var movements = await s.Reports.MovementsAsync(new MovementFilter(f.Yarn, null, null, "ПР-1"));
            Assert.Equal(["Инвентаризация ИН-000001", "Перемещение ПМ-000001", "Поступление ПТ-000001"], movements.Rows.Select(r => r.Document));
            Assert.Equal((0m, 1.5m), (movements.Rows[0].Incoming, movements.Rows[0].Outgoing));

            // Октябрь по складу пряжи: на начало 30, приход 0, расход 1,5 (недостача), на конец 28,5.
            var october = await s.Reports.TurnoverAsync(new TurnoverFilter(f.Yarn, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), null, null));
            var wool = october.Single(r => r.Code == "ПР-1");
            Assert.Equal((30m, 0m, 1.5m, 28.5m), (wool.Opening, wool.Incoming, wool.Outgoing, wool.Closing));
        }
    }

    [SqlFact]
    public async Task Reversal_reduces_turnover_instead_of_adding_opposite()
    {
        var f = await SetUpAsync();
        var receipt = await PostedAsync(f, StockOperationKind.Receipt, Sep10, f.Yarn, null, (f.Wool, 10m));
        await PostedAsync(f, StockOperationKind.Receipt, Sep10, f.Yarn, null, (f.Wool, 5m));
        await using var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId);
        await s.Documents.ReverseAsync(receipt, "Задвоили накладную", (await s.Documents.GetAsync(receipt)).RowVersion);

        var september = await s.Reports.TurnoverAsync(new TurnoverFilter(null, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null, null));
        var wool = september.Single();
        Assert.Equal((0m, 5m, 0m, 5m), (wool.Opening, wool.Incoming, wool.Outgoing, wool.Closing));
        var movements = await s.Reports.MovementsAsync(new MovementFilter(null, null, null, null));
        Assert.Contains(movements.Rows, r => r.Document == "Сторно Поступление ПТ-000001" && r.Outgoing == 10m);
    }

    [SqlFact]
    public async Task Closed_period_blocks_posting_reversal_and_direct_inserts()
    {
        var f = await SetUpAsync();
        var receipt = await PostedAsync(f, StockOperationKind.Receipt, Sep10, f.Yarn, null, (f.Wool, 10m));
        long draft;
        await using (var s = host.As(f.Senior, f.Org.OrganizationId))
        {
            draft = await s.Documents.CreateAsync(StockOperationKind.WriteOff,
                new StockDocumentHeader(f.Yarn, null, null, f.Reason("Передача в производство"), Sep20, null));
            await s.Documents.SetLineAsync(draft, f.Wool, 1m, (await s.Documents.GetAsync(draft)).RowVersion);
            Assert.False((await s.Period.GetAsync()).CanManage);
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Period.CloseAsync(new DateOnly(2026, 9, 30), null));
        }

        await using (var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId))
        {
            await s.Period.CloseAsync(new DateOnly(2026, 9, 30), null);
            Assert.Equal(new DateOnly(2026, 9, 30), (await s.Period.GetAsync()).ClosedThrough);
        }

        await using (var s = host.As(f.Senior, f.Org.OrganizationId))
        {
            var card = await s.Documents.GetAsync(draft);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => s.Documents.PostAsync(draft, card.RowVersion));
            Assert.Equal("period.closed", ex.Code);
        }

        await using (var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId))
        {
            var posted = await s.Documents.GetAsync(receipt);
            Assert.Equal("period.closed", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Documents.ReverseAsync(receipt, "Ошибка", posted.RowVersion))).Code);
        }

        await using (var db = host.NewDb())
        {
            var sql = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [kniterp].[stock_movements] ([OrganizationId], [WarehouseId], [ItemId], [Quantity], [OccurredOn], [Source], [SourceId], [CreatedAtUtc])
                VALUES ({f.Org.OrganizationId}, {f.Yarn}, {f.Wool}, 1, {new DateTime(2026, 9, 15)}, 2, 0, {host.Clock.UtcNow})
                """));
            Assert.Equal(51002, sql.Number);
        }

        await using (var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId))
        {
            var p = await s.Period.GetAsync();
            await s.Period.ReopenAsync(new DateOnly(2026, 9, 15), "Нашли накладную за 20.09", p.RowVersion);
            await s.Documents.PostAsync(draft, (await s.Documents.GetAsync(draft)).RowVersion);
            Assert.Equal(StockDocumentStatus.Posted, (await s.Documents.GetAsync(draft)).Status);
        }
    }

    private sealed record Fixture(
        CreatedOrganization Org, long Yarn, long Shop, long Wool, long Buttons, long Senior, IReadOnlyDictionary<string, long> Reasons)
    {
        public long Reason(string name) => Reasons[name];
    }

    private async Task<long> PostedAsync(
        Fixture f, StockOperationKind kind, DateOnly date, long warehouse, long? target, params (long Item, decimal Qty)[] lines)
    {
        await using var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId);
        var reason = kind switch
        {
            StockOperationKind.Receipt => "Закупка у поставщика",
            StockOperationKind.Transfer => "Перемещение между складами",
            _ => "Передача в производство",
        };
        var doc = await s.Documents.CreateAsync(kind, new StockDocumentHeader(warehouse, target, null, f.Reason(reason), date, null));
        foreach (var (item, qty) in lines)
        {
            await s.Documents.SetLineAsync(doc, item, qty, (await s.Documents.GetAsync(doc)).RowVersion);
        }

        await s.Documents.PostAsync(doc, (await s.Documents.GetAsync(doc)).RowVersion);
        return doc;
    }

    private async Task<Fixture> SetUpAsync()
    {
        var inn = NextValidInn();
        CreatedOrganization org;
        await using (var s = host.As(null, null))
        {
            org = await s.Organizations.CreateWithOwnerAsync(new CreateOrganizationCommand(
                $"Тестовая организация {inn}", $"Тест {inn}", inn, null, false, "Europe/Moscow", $"owner-{inn}@test.local", $"Владелец {inn}"));
        }

        long yarn, shop, wool, buttons;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var units = await s.Catalog.ListUnitsAsync();
            yarn = await s.Warehouses.CreateWarehouseAsync("Склад пряжи", null);
            shop = await s.Warehouses.CreateWarehouseAsync("Вязальный цех", null);
            wool = await s.Catalog.CreateItemAsync(new ItemCommand("ПР-1", "Пряжа шерсть", ItemType.RawMaterial, units.Single(u => u.Symbol == "кг").Id, null));
            buttons = await s.Catalog.CreateItemAsync(new ItemCommand("Ф-1", "Пуговица", ItemType.Accessory, units.Single(u => u.Symbol == "шт").Id, null));
        }

        await using var db = host.NewDb();
        var reasons = await db.OperationReasons.Where(r => r.OrganizationId == org.OrganizationId).ToDictionaryAsync(r => r.Name, r => r.Id);
        long senior;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            senior = (await s.Access.InviteAsync(new InviteUserCommand($"senior-{Guid.NewGuid():N}@test.local",
                "Старший кладовщик", SystemRoles.SeniorStorekeeper, null, WarehouseId: yarn))).UserId;
        }

        (await db.Users.SingleAsync(u => u.Id == senior)).Activate();
        await db.SaveChangesAsync();
        return new Fixture(org, yarn, shop, wool, buttons, senior, reasons);
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
