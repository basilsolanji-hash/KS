using KnitErp.Application.Access;
using KnitErp.Application.Catalog;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Application.Purchasing;
using KnitErp.Application.Warehousing;
using KnitErp.Domain.Access;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Purchasing;
using KnitErp.Domain.Warehousing;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>Закупки (D64): заказ с ценами и НДС, поступление и возврат по заказу, оплаты, расчёты, права на цены.</summary>
public sealed class PurchasingTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private static int _innSeed = 800_000_000;
    private static readonly DateOnly Day = new(2026, 10, 9);

    [SqlFact]
    public async Task Order_receipt_return_and_payments_make_supplier_debt()
    {
        var f = await SetUpAsync();
        long order, receipt, ret;
        await using (var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId))
        {
            var options = await s.Purchases.GetOptionsAsync(Day);
            Assert.Equal(22m, options.Items.Single(i => i.Id == f.Wool).VatPercent); // основная ставка России в 2026
            order = await s.Purchases.CreateOrderAsync(new PurchaseOrderHeader(Day, f.Supplier, f.Yarn, Day.AddDays(5), "№ 15 от 05.10.2026", true, null));
            var dto = await s.Purchases.GetOrderAsync(order);
            Assert.StartsWith("ЗП-", dto.Number);
            // 50 кг × 1 220 ₽ с НДС 22%: сумма 61 000, НДС 11 000.
            await s.Purchases.SetOrderLineAsync(order, f.Wool, 50, 1220m, 22m, dto.RowVersion);
            await s.Purchases.SetOrderLineAsync(order, f.Buttons, 200, 5m, null, (await s.Purchases.GetOrderAsync(order)).RowVersion);
            dto = await s.Purchases.GetOrderAsync(order);
            Assert.Equal((62_000m, 11_000m), (dto.Total!.Value, dto.VatTotal!.Value));

            Assert.Equal("purchase.order.not_confirmed", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Purchases.CreateReceiptAsync(order))).Code);
            await s.Purchases.ConfirmOrderAsync(order, dto.RowVersion);
            Assert.Equal("purchase.not_draft", (await Assert.ThrowsAsync<BusinessRuleException>(async () =>
                await s.Purchases.SetOrderLineAsync(order, f.Wool, 1, 1, null, (await s.Purchases.GetOrderAsync(order)).RowVersion))).Code);

            // Поступление по заказу: черновик со всем заказанным; привезли 40 кг пряжи и все пуговицы.
            receipt = await s.Purchases.CreateReceiptAsync(order);
            var doc = await s.Documents.GetAsync(receipt);
            Assert.Equal((order, 2), (doc.PurchaseOrderId!.Value, doc.Lines.Count));
            await s.Documents.SetLineAsync(receipt, f.Wool, 40, doc.RowVersion);
            await s.Documents.PostAsync(receipt, (await s.Documents.GetAsync(receipt)).RowVersion);

            dto = await s.Purchases.GetOrderAsync(order);
            Assert.Equal(ReceiptState.Partial, dto.Received);
            Assert.Equal(10m, dto.Lines.Single(l => l.ItemId == f.Wool).Left);
            Assert.Equal(49_800m, dto.ReceivedValue); // 40 × 1 220 + 200 × 5

            // Возврат: больше полученного нельзя, 5 кг — можно.
            ret = await s.Purchases.CreateReturnAsync(order);
            var r = await s.Documents.GetAsync(ret);
            Assert.Equal(StockOperationKind.ReturnToSupplier, r.Kind);
            await s.Documents.RemoveLineAsync(ret, f.Buttons, r.RowVersion);
            await s.Documents.SetLineAsync(ret, f.Wool, 45, (await s.Documents.GetAsync(ret)).RowVersion);
            Assert.Equal("purchase.return.exceeds", (await Assert.ThrowsAsync<BusinessRuleException>(async () =>
                await s.Documents.PostAsync(ret, (await s.Documents.GetAsync(ret)).RowVersion))).Code);
            await s.Documents.SetLineAsync(ret, f.Wool, 5, (await s.Documents.GetAsync(ret)).RowVersion);
            await s.Documents.PostAsync(ret, (await s.Documents.GetAsync(ret)).RowVersion);
            Assert.Equal(35m, (await s.Stock.BalancesAsync(new StockFilter(f.Yarn))).Single(b => b.ItemId == f.Wool).Quantity);

            // Оплаты: 30 000 по заказу и ошибочная 1 000, отменённая с причиной.
            await s.Purchases.CreatePaymentAsync(Day, f.Supplier, order, 30_000m, "п/п 101");
            var wrong = await s.Purchases.CreatePaymentAsync(Day, f.Supplier, null, 1_000m, null);
            var wrongDto = (await s.Purchases.ListPaymentsAsync()).Single(p => p.Id == wrong);
            Assert.Equal("field.required", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Purchases.CancelPaymentAsync(wrong, " ", wrongDto.RowVersion))).Code);
            await s.Purchases.CancelPaymentAsync(wrong, "ошибка суммы", wrongDto.RowVersion);

            dto = await s.Purchases.GetOrderAsync(order);
            Assert.Equal((49_800m, 6_100m, 30_000m, 13_700m), (dto.ReceivedValue!.Value, dto.ReturnedValue!.Value, dto.Paid!.Value, dto.Debt!.Value));
            var balance = (await s.Purchases.BalancesAsync()).Single(b => b.SupplierId == f.Supplier);
            Assert.Equal(13_700m, balance.Debt);
            Assert.Equal(0m, (await s.Purchases.BalancesAsync(Day.AddDays(-1))).SingleOrDefault(b => b.SupplierId == f.Supplier)?.Debt ?? 0m);

            Assert.Equal("purchase.order.in_use", (await Assert.ThrowsAsync<BusinessRuleException>(async () =>
                await s.Purchases.CancelOrderAsync(order, (await s.Purchases.GetOrderAsync(order)).RowVersion))).Code);
            await s.Purchases.CloseOrderAsync(order, dto.RowVersion);
            Assert.Equal(PurchaseOrderStatus.Closed, (await s.Purchases.ListOrdersAsync(new PurchaseOrderFilter(Search: "Пряжа"))).Single().Status);
        }

        // Старший кладовщик видит заказ без цен и не может его менять; кладовщик заказов не видит.
        await using (var s = host.As(f.Senior, f.Org.OrganizationId))
        {
            var dto = await s.Purchases.GetOrderAsync(order);
            Assert.Null(dto.Total);
            Assert.All(dto.Lines, l => Assert.Null(l.Price));
            Assert.False(dto.CanEdit);
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Purchases.BalancesAsync());
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Purchases.CreateOrderAsync(new PurchaseOrderHeader(Day, f.Supplier, f.Yarn, null, null, true, null)));
        }

        await using (var s = host.As(f.Keeper, f.Org.OrganizationId))
        {
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Purchases.ListOrdersAsync(new PurchaseOrderFilter()));
        }

        // Чужая организация заказа не видит.
        var other = await CreateOrgAsync();
        await using (var s = host.As(other.OwnerUserId, other.OrganizationId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => s.Purchases.GetOrderAsync(order));
        }

        await using var db = host.NewDb();
        Assert.True(await db.AuditEntries.AnyAsync(e => e.EntityType == "PurchaseOrder" && e.EntityId == order.ToString() && e.Action == "purchase.order.confirmed"));
    }

    private sealed record Fixture(CreatedOrganization Org, long Yarn, long Wool, long Buttons, long Supplier, long Senior, long Keeper);

    private async Task<Fixture> SetUpAsync()
    {
        var org = await CreateOrgAsync();
        long yarn, wool, buttons, supplier;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var units = await s.Catalog.ListUnitsAsync();
            yarn = await s.Warehouses.CreateWarehouseAsync("Склад пряжи", null);
            wool = await s.Catalog.CreateItemAsync(new ItemCommand("ПР-1", "Пряжа шерсть", ItemType.RawMaterial, units.Single(u => u.Symbol == "кг").Id, null));
            buttons = await s.Catalog.CreateItemAsync(new ItemCommand("Ф-1", "Пуговица", ItemType.Accessory, units.Single(u => u.Symbol == "шт").Id, null));
            supplier = await s.Counterparties.CreateAsync(new CounterpartyCommand("ООО «Пряжа»", null, null, true, false, null));
        }

        return new Fixture(org, yarn, wool, buttons, supplier,
            await InviteActiveAsync(org, SystemRoles.SeniorStorekeeper, yarn), await InviteActiveAsync(org, SystemRoles.Storekeeper, yarn));
    }

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
