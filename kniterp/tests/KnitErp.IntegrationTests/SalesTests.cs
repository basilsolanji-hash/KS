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

/// <summary>Продажи (D65): заказ покупателя, отгрузка и возврат по заказу, оплаты, долг покупателя, права.</summary>
public sealed class SalesTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private static int _innSeed = 810_000_000;
    private static readonly DateOnly Day = new(2026, 10, 9);

    [SqlFact]
    public async Task Order_shipment_return_and_payments_make_customer_debt()
    {
        var f = await SetUpAsync();
        long order;
        await using (var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId))
        {
            // На складе 30 свитеров (поступление без заказа).
            var receipt = await s.Documents.CreateAsync(StockOperationKind.Receipt, new StockDocumentHeader(f.Store, null, null, await Reason(StockOperationKind.Receipt), Day, null));
            await s.Documents.SetLineAsync(receipt, f.Sweater, 30, (await s.Documents.GetAsync(receipt)).RowVersion);
            await s.Documents.PostAsync(receipt, (await s.Documents.GetAsync(receipt)).RowVersion);

            order = await s.Sales.CreateOrderAsync(new SalesOrderHeader(Day, f.Customer, f.Store, Day.AddDays(2), "Договор № 7", false, null));
            var dto = await s.Sales.GetOrderAsync(order);
            Assert.StartsWith("ЗК-", dto.Number);
            // Цены без НДС: 25 × 2 000 = 50 000 + НДС 22% 11 000 = 61 000.
            await s.Sales.SetOrderLineAsync(order, f.Sweater, 25, 2000m, 22m, dto.RowVersion);
            dto = await s.Sales.GetOrderAsync(order);
            Assert.Equal((61_000m, 11_000m), (dto.Total!.Value, dto.VatTotal!.Value));
            await s.Sales.ConfirmOrderAsync(order, dto.RowVersion);

            // Отгрузка по заказу: 25 со склада; остаток склада — 5.
            var ship = await s.Sales.CreateShipmentAsync(order);
            var doc = await s.Documents.GetAsync(ship);
            Assert.Equal((StockOperationKind.Shipment, order, 25m), (doc.Kind, doc.SalesOrderId!.Value, doc.Lines.Single().Quantity));
            Assert.Equal(30m, doc.Lines.Single().Available);
            await s.Documents.PostAsync(ship, doc.RowVersion);
            Assert.Equal(5m, (await s.Stock.BalancesAsync(new StockFilter(f.Store))).Single(b => b.ItemId == f.Sweater).Quantity);
            dto = await s.Sales.GetOrderAsync(order);
            Assert.Equal((ShipmentState.Full, 61_000m), (dto.Shipped, dto.ShippedValue!.Value));
            Assert.Equal("sales.order.shipped", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Sales.CreateShipmentAsync(order))).Code);

            // Возврат 2 шт (брак) — больше отгруженного нельзя.
            var ret = await s.Sales.CreateReturnAsync(order);
            await s.Documents.SetLineAsync(ret, f.Sweater, 26, (await s.Documents.GetAsync(ret)).RowVersion);
            Assert.Equal("sales.return.exceeds", (await Assert.ThrowsAsync<BusinessRuleException>(async () =>
                await s.Documents.PostAsync(ret, (await s.Documents.GetAsync(ret)).RowVersion))).Code);
            await s.Documents.SetLineAsync(ret, f.Sweater, 2, (await s.Documents.GetAsync(ret)).RowVersion);
            await s.Documents.PostAsync(ret, (await s.Documents.GetAsync(ret)).RowVersion);
            Assert.Equal(7m, (await s.Stock.BalancesAsync(new StockFilter(f.Store))).Single(b => b.ItemId == f.Sweater).Quantity);

            await s.Sales.CreatePaymentAsync(Day, f.Customer, order, 40_000m, "п/п 55");
            dto = await s.Sales.GetOrderAsync(order);
            Assert.Equal((61_000m, 4_880m, 40_000m, 16_120m), (dto.ShippedValue!.Value, dto.ReturnedValue!.Value, dto.Paid!.Value, dto.Debt!.Value));
            Assert.Equal(16_120m, (await s.Sales.BalancesAsync()).Single(b => b.CustomerId == f.Customer).Debt);
        }

        await using (var s = host.As(f.Senior, f.Org.OrganizationId))
        {
            var dto = await s.Sales.GetOrderAsync(order);
            Assert.Null(dto.Total);
            Assert.False(dto.CanEdit);
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Sales.BalancesAsync());
        }

        var other = await CreateOrgAsync();
        await using (var s = host.As(other.OwnerUserId, other.OrganizationId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => s.Sales.GetOrderAsync(order));
        }

        async Task<long?> Reason(StockOperationKind kind)
        {
            await using var db = host.NewDb();
            return await db.OperationReasons.Where(r => r.OrganizationId == f.Org.OrganizationId && r.Kind == kind).Select(r => (long?)r.Id).FirstAsync();
        }
    }

    private sealed record Fixture(CreatedOrganization Org, long Store, long Sweater, long Customer, long Senior);

    private async Task<Fixture> SetUpAsync()
    {
        var org = await CreateOrgAsync();
        long store, sweater, customer;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var units = await s.Catalog.ListUnitsAsync();
            store = await s.Warehouses.CreateWarehouseAsync("Склад готовой продукции", null);
            sweater = await s.Catalog.CreateItemAsync(new ItemCommand("СВ-1", "Свитер", ItemType.Finished, units.Single(u => u.Symbol == "шт").Id, null));
            customer = await s.Counterparties.CreateAsync(new CounterpartyCommand("ООО «Магазин»", null, null, false, true, null));
        }

        long senior;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            senior = (await s.Access.InviteAsync(new InviteUserCommand($"senior-{Guid.NewGuid():N}@test.local", "Старший кладовщик",
                SystemRoles.SeniorStorekeeper, null, WarehouseId: store))).UserId;
        }

        await using (var db = host.NewDb())
        {
            (await db.Users.SingleAsync(u => u.Id == senior)).Activate();
            await db.SaveChangesAsync();
        }

        return new Fixture(org, store, sweater, customer, senior);
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
