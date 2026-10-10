using KnitErp.Application.Access;
using KnitErp.Application.Catalog;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Application.Sales;
using KnitErp.Application.Warehousing;
using KnitErp.Domain.Access;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Sales;
using KnitErp.Domain.Warehousing;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>
/// Повторная проверка аудита 10.10.2026 (п. 1–3): устаревшая форма не меняет проведённый документ, сторно гасит движения до нуля,
/// параллельные возвраты не превышают отгрузку, параллельная смена ролей не оставляет организацию без Владельца.
/// </summary>
public sealed class AuditConcurrencyTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private static int _innSeed = 850_000_000;
    private static readonly DateOnly Day = new(2026, 10, 9);

    [SqlFact]
    public async Task Stale_form_cannot_change_posted_document_and_reversal_returns_stock_to_zero()
    {
        var (org, store, item, reason) = await SetUpAsync();
        long doc;
        byte[] staleVersion;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            doc = await s.Documents.CreateAsync(StockOperationKind.Receipt, new StockDocumentHeader(store, null, null, reason, Day, null));
            await s.Documents.SetLineAsync(doc, item, 10, (await s.Documents.GetAsync(doc)).RowVersion);
            staleVersion = (await s.Documents.GetAsync(doc)).RowVersion;
        }

        // Форма открыта (черновик загружен), в это время документ проводят в другой вкладке.
        await using var stale = host.NewDb();
        var draft = await stale.StockDocuments.Include(d => d.Lines).SingleAsync(d => d.Id == doc);
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            await s.Documents.PostAsync(doc, staleVersion);
        }

        draft.SetLine(item, 7);
        await Assert.ThrowsAnyAsync<Exception>(() => stale.SaveChangesAsync());

        // Та же версия через сервис — конфликт или «только черновик»; строки в БД не тронуты даже прямым SQL.
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => s.Documents.SetLineAsync(doc, item, 7, staleVersion));
            await using var raw = host.NewDb();
            await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
                raw.Database.ExecuteSqlRawAsync("UPDATE kniterp.stock_document_lines SET Quantity = 7 WHERE DocumentId = {0}", doc));
            Assert.Equal(10m, (await s.Documents.GetAsync(doc)).Lines.Single().Quantity);

            await s.Documents.ReverseAsync(doc, "Ошибка ввода", (await s.Documents.GetAsync(doc)).RowVersion);
            Assert.Equal(0m, await s.Db.StockMovements.Where(m => m.OrganizationId == org.OrganizationId && m.ItemId == item).SumAsync(m => m.Quantity));
        }
    }

    [SqlFact]
    public async Task Parallel_customer_returns_cannot_exceed_shipment()
    {
        var (org, store, item, reason) = await SetUpAsync();
        long order, first, second;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var stock = await s.Documents.CreateAsync(StockOperationKind.Receipt, new StockDocumentHeader(store, null, null, reason, Day, null));
            await s.Documents.SetLineAsync(stock, item, 20, (await s.Documents.GetAsync(stock)).RowVersion);
            await s.Documents.PostAsync(stock, (await s.Documents.GetAsync(stock)).RowVersion);
            var customer = await s.Counterparties.CreateAsync(new CounterpartyCommand("ООО «Покупатель»", null, null, false, true, null));
            order = await s.Sales.CreateOrderAsync(new SalesOrderHeader(Day, customer, store, Day, null, false, null));
            await s.Sales.SetOrderLineAsync(order, item, 10, 100m, 22m, (await s.Sales.GetOrderAsync(order)).RowVersion);
            await s.Sales.ConfirmOrderAsync(order, (await s.Sales.GetOrderAsync(order)).RowVersion);
            var ship = await s.Sales.CreateShipmentAsync(order);
            await s.Documents.SetLineAsync(ship, item, 10, (await s.Documents.GetAsync(ship)).RowVersion);
            await s.Documents.PostAsync(ship, (await s.Documents.GetAsync(ship)).RowVersion);
            first = await s.Sales.CreateReturnAsync(order);
            await s.Documents.SetLineAsync(first, item, 10, (await s.Documents.GetAsync(first)).RowVersion);
            second = await s.Sales.CreateReturnAsync(order);
            await s.Documents.SetLineAsync(second, item, 10, (await s.Documents.GetAsync(second)).RowVersion);
        }

        async Task<bool> PostAsync(long id)
        {
            await using var s = host.As(org.OwnerUserId, org.OrganizationId);
            try
            {
                await s.Documents.PostAsync(id, (await s.Documents.GetAsync(id)).RowVersion);
                return true;
            }
            catch (BusinessRuleException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(PostAsync(first), PostAsync(second));
        Assert.Equal(1, results.Count(r => r));
        await using var db = host.NewDb();
        Assert.Equal(1, await db.StockDocuments.CountAsync(d => d.SalesOrderId == order && d.Kind == StockOperationKind.CustomerReturn
                                                                && d.Status == StockDocumentStatus.Posted));
    }

    [SqlFact]
    public async Task Two_owners_demoting_each_other_in_parallel_leave_one_owner()
    {
        var (org, _, _, _) = await SetUpAsync();
        long second;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            second = (await s.Access.InviteAsync(new InviteUserCommand($"owner2-{Guid.NewGuid():N}@test.local", "Второй владелец",
                SystemRoles.Owner, "Совладелец"))).UserId;
        }

        await using (var db = host.NewDb())
        {
            (await db.Users.SingleAsync(u => u.Id == second)).Activate();
            await db.SaveChangesAsync();
        }

        async Task<bool> DemoteAsync(long actor, long target)
        {
            await using var s = host.As(actor, org.OrganizationId);
            try
            {
                await s.Access.ChangeRoleAsync(new ChangeRoleCommand(target, SystemRoles.Storekeeper, "Передача дел"));
                return true;
            }
            catch (Exception ex) when (ex is BusinessRuleException or AccessDeniedException or ConcurrencyConflictException)
            {
                return false;
            }
        }

        for (var round = 0; round < 3; round++)
        {
            await Task.WhenAll(DemoteAsync(org.OwnerUserId, second), DemoteAsync(second, org.OwnerUserId));
            await using var db = host.NewDb();
            var owners = await db.RoleAssignments.Where(a => a.OrganizationId == org.OrganizationId && a.RevokedAtUtc == null)
                .Join(db.Roles.Where(r => r.Code == SystemRoles.Owner), a => a.RoleId, r => (long?)r.Id, (a, r) => a.UserId).CountAsync();
            Assert.True(owners >= 1, "Организация осталась без Владельца.");
        }
    }

    private async Task<(CreatedOrganization Org, long Store, long Item, long? Reason)> SetUpAsync()
    {
        int[] w = [2, 4, 10, 3, 5, 9, 4, 6, 8];
        var body = Interlocked.Increment(ref _innSeed).ToString("D9");
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (body[i] - '0') * w[i];
        }

        var inn = body + (sum % 11 % 10);
        CreatedOrganization org;
        await using (var s = host.As(null, null))
        {
            org = await s.Organizations.CreateWithOwnerAsync(new CreateOrganizationCommand(
                $"Тестовая организация {inn}", $"Тест {inn}", inn, null, false, "Europe/Moscow", $"owner-{inn}-{Guid.NewGuid():N}@test.local", $"Владелец {inn}", "RU"));
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var unit = (await s.Catalog.ListUnitsAsync()).Single(u => u.Symbol == "шт").Id;
            var store = await s.Warehouses.CreateWarehouseAsync("Склад", null);
            var item = await s.Catalog.CreateItemAsync(new ItemCommand("СВ-1", "Свитер", ItemType.Finished, unit, null));
            var reason = await s.Db.OperationReasons.Where(r => r.OrganizationId == org.OrganizationId && r.Kind == StockOperationKind.Receipt)
                .Select(r => (long?)r.Id).FirstAsync();
            return (org, store, item, reason);
        }
    }
}
