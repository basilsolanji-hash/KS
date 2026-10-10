using KnitErp.Application.Access;
using KnitErp.Application.Catalog;
using KnitErp.Application.Common;
using KnitErp.Application.Finance;
using KnitErp.Application.Organizations;
using KnitErp.Application.Warehousing;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Finance;
using KnitErp.Domain.Organizations;
using KnitErp.Domain.Purchasing;
using KnitErp.Domain.Sales;
using KnitErp.Domain.Warehousing;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>D85: разноска платежей по заказам, авансы, кассовые ордера к оплатам, акт сверки.</summary>
public sealed class PaymentAllocationTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private static int _innSeed = 840_000_000;
    private static readonly DateOnly Day = new(2026, 10, 9);

    [SqlFact]
    public async Task Payments_allocate_to_orders_advances_are_offset_and_cancel_releases()
    {
        var f = await SeedAsync();
        await using var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId);

        // Два заказа: А на 1 000 (отгрузить раньше), Б на 500. Оплата 1 200 без заказа — вся аванс.
        var a = await OrderAsync(s, f, 10, 100m, Day.AddDays(1));
        var b = await OrderAsync(s, f, 5, 100m, Day.AddDays(5));
        var pay = await s.Sales.CreatePaymentAsync(Day, f.Customer, null, 1_200m, null);
        var view = await s.Sales.GetPaymentAllocationsAsync(pay);
        Assert.Equal((1_200m, 0m), (view.Unallocated, view.Allocated));
        Assert.Equal([a, b], view.OpenOrders.Select(o => o.Id));
        Assert.Equal(1_200m, (await s.Sales.ListPaymentsAsync()).Single(p => p.Id == pay).Unallocated);
        Assert.Equal(1_200m, await s.Sales.AvailableAdvancesAsync(a));

        // Автоматически: сначала А (ранний срок) целиком, остаток — на Б.
        Assert.Equal(1_200m, await s.Sales.AutoAllocatePaymentAsync(pay));
        Assert.Equal(1_000m, (await s.Sales.GetOrderAsync(a)).Paid);
        Assert.Equal(200m, (await s.Sales.GetOrderAsync(b)).Paid);
        Assert.Equal("ЗК-000001, ЗК-000002".Split(", ").Length, (await s.Sales.ListPaymentsAsync()).Single(p => p.Id == pay).Orders!.Split(", ").Length);

        // Оплата по заказу Б больше его остатка (300): 300 — на заказ, 100 — аванс.
        var pay2 = await s.Sales.CreatePaymentAsync(Day, f.Customer, b, 400m, null);
        Assert.Equal(500m, (await s.Sales.GetOrderAsync(b)).Paid);
        Assert.Equal(100m, (await s.Sales.GetPaymentAllocationsAsync(pay2)).Unallocated);

        // Вручную нельзя больше остатка заказа и больше неразнесённого в оплате.
        var c = await OrderAsync(s, f, 1, 50m, Day.AddDays(9));
        Assert.Equal("payment.allocation.order_exceeded", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Sales.AllocatePaymentAsync(pay2, b, 1m))).Code);
        Assert.Equal("payment.allocation.payment_exceeded", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Sales.AllocatePaymentAsync(pay, c, 10m))).Code);

        // Снять разноску А — 1 000 вернулись в аванс; зачесть авансы в заказ В — 50 из самой старой оплаты.
        var alloc = (await s.Sales.GetPaymentAllocationsAsync(pay)).Allocations.Single(x => x.OrderId == a);
        await s.Sales.RemovePaymentAllocationAsync(alloc.Id, alloc.RowVersion);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => s.Sales.RemovePaymentAllocationAsync(alloc.Id, alloc.RowVersion));
        Assert.Equal(0m, (await s.Sales.GetOrderAsync(a)).Paid);
        Assert.Equal(50m, await s.Sales.ApplyAdvancesAsync(c));
        Assert.Equal(50m, (await s.Sales.GetOrderAsync(c)).Paid);
        Assert.Equal(950m, (await s.Sales.GetPaymentAllocationsAsync(pay)).Unallocated);

        // Повторная разноска на тот же заказ складывается в одну строку.
        await s.Sales.AllocatePaymentAsync(pay, a, 300m);
        await s.Sales.AllocatePaymentAsync(pay, a, 200m);
        Assert.Equal(500m, (await s.Sales.GetPaymentAllocationsAsync(pay)).Allocations.Single(x => x.OrderId == a).Amount);

        // Заказ с разнесённой оплатой не отменить; отмена оплаты освобождает заказ — оплачено снова 0.
        var orderA = await s.Sales.GetOrderAsync(a);
        Assert.Equal("sales.order.in_use", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Sales.CancelOrderAsync(a, orderA.RowVersion))).Code);
        var payRow = (await s.Sales.ListPaymentsAsync()).Single(p => p.Id == pay);
        await s.Sales.CancelPaymentAsync(pay, "ошибка", payRow.RowVersion);
        Assert.Equal(0m, (await s.Sales.GetOrderAsync(a)).Paid);
        Assert.Equal("payment.allocation.cancelled", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Sales.AutoAllocatePaymentAsync(pay))).Code);
        await s.Sales.CancelOrderAsync(a, (await s.Sales.GetOrderAsync(a)).RowVersion);

        // Аудит разноски записан.
        Assert.True(await s.Db.AuditEntries.AnyAsync(x => x.OrganizationId == f.Org.OrganizationId && x.Action == KnitErp.Domain.Audit.AuditActions.PaymentAllocated));
        Assert.True(await s.Db.AuditEntries.AnyAsync(x => x.OrganizationId == f.Org.OrganizationId
                                                          && x.Action == KnitErp.Domain.Audit.AuditActions.PaymentAllocationRemoved));

        // Чужая организация — «не найдено».
        var other = await CreateOrgAsync();
        await using var o = host.As(other.OwnerUserId, other.OrganizationId);
        await Assert.ThrowsAsync<NotFoundException>(() => o.Sales.GetPaymentAllocationsAsync(pay2));
        await Assert.ThrowsAsync<NotFoundException>(() => o.Sales.AllocatePaymentAsync(pay2, b, 1m));
        await Assert.ThrowsAsync<NotFoundException>(() => o.Sales.ApplyAdvancesAsync(b));
    }

    [SqlFact]
    public async Task Concurrent_allocations_never_exceed_the_order()
    {
        var f = await SeedAsync();
        long order, p1, p2;
        await using (var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId))
        {
            order = await OrderAsync(s, f, 5, 100m, Day);
            p1 = await s.Sales.CreatePaymentAsync(Day, f.Customer, null, 400m, null);
            p2 = await s.Sales.CreatePaymentAsync(Day, f.Customer, null, 400m, null);
        }

        // Две вкладки разносят 400 + 400 на заказ в 500 одновременно: одна успевает, вторая получает отказ.
        async Task<string?> Allocate(long payment)
        {
            await using var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId);
            try
            {
                await s.Sales.AllocatePaymentAsync(payment, order, 400m);
                return null;
            }
            catch (BusinessRuleException ex)
            {
                return ex.Code;
            }
        }

        var results = await Task.WhenAll(Allocate(p1), Allocate(p2));
        Assert.Single(results, r => r is null);
        Assert.Single(results, r => r == "payment.allocation.order_exceeded");
        await using var check = host.As(f.Org.OwnerUserId, f.Org.OrganizationId);
        Assert.Equal(400m, (await check.Sales.GetOrderAsync(order)).Paid);
    }

    [SqlFact]
    public async Task Cash_payments_get_cash_order_numbers_and_print_with_vat()
    {
        var f = await SeedAsync();
        await using var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId);
        var ooo = (await s.LegalEntities.ListAsync()).Single();
        var cash = await s.LegalEntities.AddCashAsync(ooo.Id, "Касса");
        var order = await OrderAsync(s, f, 10, 122m, Day);

        // Оплата покупателя в кассу — ПКО-000001; следующий приход в кассу операцией — ПКО-000002: нумерация общая.
        var pay = await s.Sales.CreatePaymentAsync(Day, f.Customer, order, 1_220m, null, null, cash);
        var row = (await s.Sales.ListPaymentsAsync()).Single(p => p.Id == pay);
        Assert.Equal("ПКО-000001", row.CashOrderNumber);
        var op = await s.MoneyOperations.CreateAsync(new MoneyOperationCommand(Day, MoneyOperationKind.Income, cash, null, 10m, null, "Размен", null));
        Assert.Equal("ПКО-000002", (await s.MoneyOperations.GetAsync(op)).Number);

        // КО-1 к оплате: основание — заказ, НДС по ставке заказа 22% (1 220 × 22 / 122 = 220).
        var ko1 = await s.Sales.PaymentCashOrderAsync(pay);
        Assert.False(ko1.Expense);
        Assert.Equal(("ПКО-000001", "ООО «Магазин»"), (ko1.Number, ko1.Party));
        Assert.Contains("заказу", ko1.Basis);
        Assert.Equal("НДС 22% — 220,00 руб.", ko1.Vat.Replace(' ', ' ').Replace(' ', ' '));

        // Оплата на расчётный счёт — без кассового ордера.
        var bankPay = await s.Sales.CreatePaymentAsync(Day, f.Customer, null, 5m, null);
        Assert.Null((await s.Sales.ListPaymentsAsync()).Single(p => p.Id == bankPay).CashOrderNumber);
        Assert.Equal("money.print.not_cash", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Sales.PaymentCashOrderAsync(bankPay))).Code);

        // Оплата поставщику из кассы — РКО-000001, КО-2; касса в минус не уходит.
        var po = await s.Purchases.CreateOrderAsync(new PurchaseOrderHeader(Day, f.Supplier, f.Store, null, "№ 7", true, null));
        await s.Purchases.SetOrderLineAsync(po, f.Wool, 1, 100m, null, (await s.Purchases.GetOrderAsync(po)).RowVersion);
        await s.Purchases.ConfirmOrderAsync(po, (await s.Purchases.GetOrderAsync(po)).RowVersion);
        Assert.Equal("money.cash.insufficient", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Purchases.CreatePaymentAsync(Day, f.Supplier, po, 5_000m, null, cash))).Code);
        var supPay = await s.Purchases.CreatePaymentAsync(Day, f.Supplier, po, 100m, null, cash);
        var ko2 = await s.Purchases.PaymentCashOrderAsync(supPay);
        Assert.Equal((true, "РКО-000001", "без налога (НДС)"), (ko2.Expense, ko2.Number, ko2.Vat));
        Assert.Contains("№ 7", ko2.Basis);
        Assert.Equal(100m, (await s.Purchases.GetOrderAsync(po)).Paid);

        // Поиск по номеру ордера.
        Assert.Single(await s.Sales.ListPaymentsAsync(search: "ПКО-000001"));
    }

    [SqlFact]
    public async Task Settlement_statement_has_opening_turnover_and_closing_by_legal_entity()
    {
        var f = await SeedAsync();
        await using var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId);

        // До периода: отгрузка 1 000, оплата 400 → сальдо на начало 600 в нашу пользу.
        var before = await OrderAsync(s, f, 10, 100m, Day.AddDays(-10), Day.AddDays(-10));
        await ShipAsync(s, f, before, 10, Day.AddDays(-10));
        await s.Sales.CreatePaymentAsync(Day.AddDays(-9), f.Customer, before, 400m, null, "55");

        // В периоде: отгрузка 300, возврат 100, оплата 600; закупка у того же контрагента-поставщика нет.
        var inPeriod = await OrderAsync(s, f, 3, 100m, Day);
        await ShipAsync(s, f, inPeriod, 3, Day);
        var back = await s.Sales.CreateReturnAsync(inPeriod);
        await s.Documents.SetLineAsync(back, f.Sweater, 1, (await s.Documents.GetAsync(back)).RowVersion);
        await s.Documents.PostAsync(back, (await s.Documents.GetAsync(back)).RowVersion);
        await s.Sales.CreatePaymentAsync(Day, f.Customer, null, 600m, null);

        var act = await s.Settlements.BuildAsync(f.Customer, null, Day.AddDays(-1), Day.AddDays(1));
        Assert.Equal(600m, act.Opening);
        Assert.Equal((300m, 700m), (act.DebitTotal, act.CreditTotal));
        Assert.Equal(200m, act.Closing);
        Assert.Contains(act.Rows, r => r.Document.StartsWith("Отгрузка (УПД)") && r.Debit == 300m);
        Assert.Contains(act.Rows, r => r.Document.StartsWith("Возврат от покупателя") && r.Credit == 100m);

        // Итог акта на конец сходится с отчётом расчётов.
        var balance = (await s.Sales.BalancesAsync(Day.AddDays(1))).Single(b => b.CustomerId == f.Customer);
        Assert.Equal(balance.Debt, act.Closing);

        // По второму юрлицу (ИП) — у контрагента расчётов нет.
        var ip = await s.LegalEntities.CreateAsync(new LegalEntityData(LegalEntityKind.SoleProprietor,
            "Индивидуальный предприниматель Петров Пётр Петрович", "ИП Петров П. П.", "500100732259", null, "304500116000157",
            null, null, "Петров П. П.", null, VatExempt: true));
        var actIp = await s.Settlements.BuildAsync(f.Customer, ip, Day.AddDays(-30), Day.AddDays(1));
        Assert.Equal((0m, 0), (actIp.Closing, actIp.Rows.Count));

        // Неверный период; чужая организация — «не найдено».
        Assert.Equal("settlement.period", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Settlements.BuildAsync(f.Customer, null, Day, Day.AddDays(-1)))).Code);
        Assert.Contains(await s.Settlements.OptionsAsync() is var opts ? opts.Counterparties : [], c => c.Id == f.Customer);
        var other = await CreateOrgAsync();
        await using var o = host.As(other.OwnerUserId, other.OrganizationId);
        await Assert.ThrowsAsync<NotFoundException>(() => o.Settlements.BuildAsync(f.Customer, null, Day, Day));
    }

    [SqlFact]
    public async Task Payment_to_other_legal_entity_cash_cannot_be_allocated_to_order()
    {
        var f = await SeedAsync();
        await using var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId);
        var ip = await s.LegalEntities.CreateAsync(new LegalEntityData(LegalEntityKind.SoleProprietor,
            "Индивидуальный предприниматель Петров Пётр Петрович", "ИП Петров П. П.", "500100732259", null, "304500116000157",
            null, null, "Петров П. П.", null, VatExempt: true));
        var ipCash = await s.LegalEntities.AddCashAsync(ip, "Касса ИП");
        var order = await OrderAsync(s, f, 1, 100m, Day);
        var pay = await s.Sales.CreatePaymentAsync(Day, f.Customer, null, 100m, null, null, ipCash);

        // Заказ ООО, деньги в кассе ИП: ни вручную, ни автоматически, ни зачётом аванса.
        Assert.Equal("payment.allocation.entity", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Sales.AllocatePaymentAsync(pay, order, 100m))).Code);
        Assert.Equal(0m, await s.Sales.AutoAllocatePaymentAsync(pay));
        Assert.Equal(0m, await s.Sales.AvailableAdvancesAsync(order));
        Assert.Equal(0m, await s.Sales.ApplyAdvancesAsync(order));
    }

    private static async Task<long> OrderAsync(Services s, Fixture f, decimal quantity, decimal price, DateOnly shipDate, DateOnly? orderDate = null)
    {
        var id = await s.Sales.CreateOrderAsync(new SalesOrderHeader(orderDate ?? Day, f.Customer, f.Store, shipDate, null, true, null));
        await s.Sales.SetOrderLineAsync(id, f.Sweater, quantity, price, 22m, (await s.Sales.GetOrderAsync(id)).RowVersion);
        await s.Sales.ConfirmOrderAsync(id, (await s.Sales.GetOrderAsync(id)).RowVersion);
        return id;
    }

    private static async Task ShipAsync(Services s, Fixture f, long order, decimal quantity, DateOnly date)
    {
        var ship = await s.Sales.CreateShipmentAsync(order);
        var doc = await s.Documents.GetAsync(ship);
        await s.Documents.UpdateHeaderAsync(ship, new StockDocumentHeader(doc.WarehouseId, null, doc.CounterpartyId, doc.ReasonId, date, null, SalesOrderId: doc.SalesOrderId), doc.RowVersion);
        await s.Documents.SetLineAsync(ship, f.Sweater, quantity, (await s.Documents.GetAsync(ship)).RowVersion);
        await s.Documents.PostAsync(ship, (await s.Documents.GetAsync(ship)).RowVersion);
    }

    private sealed record Fixture(CreatedOrganization Org, long Store, long Customer, long Supplier, long Sweater, long Wool);

    private async Task<Fixture> SeedAsync()
    {
        var org = await CreateOrgAsync();
        await using var s = host.As(org.OwnerUserId, org.OrganizationId);
        var units = await s.Catalog.ListUnitsAsync();
        var store = await s.Warehouses.CreateWarehouseAsync("Склад", null);
        var sweater = await s.Catalog.CreateItemAsync(new ItemCommand("СВ-1", "Свитер", ItemType.Finished, units.Single(u => u.Symbol == "шт").Id, null));
        var wool = await s.Catalog.CreateItemAsync(new ItemCommand("ПР-1", "Пряжа", ItemType.RawMaterial, units.Single(u => u.Symbol == "кг").Id, null));
        var customer = await s.Counterparties.CreateAsync(new CounterpartyCommand("ООО «Магазин»", null, null, false, true, null));
        var supplier = await s.Counterparties.CreateAsync(new CounterpartyCommand("ООО «Пряжа»", null, null, true, false, null));

        // Свитера на складе задним числом — для отгрузок в любом тесте.
        var reason = await s.Db.OperationReasons.Where(r => r.OrganizationId == org.OrganizationId && r.Kind == StockOperationKind.Receipt)
            .Select(r => (long?)r.Id).FirstAsync();
        var stock = await s.Documents.CreateAsync(StockOperationKind.Receipt, new StockDocumentHeader(store, null, null, reason, Day.AddDays(-60), null));
        await s.Documents.SetLineAsync(stock, sweater, 100, (await s.Documents.GetAsync(stock)).RowVersion);
        await s.Documents.PostAsync(stock, (await s.Documents.GetAsync(stock)).RowVersion);
        return new Fixture(org, store, customer, supplier, sweater, wool);
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
