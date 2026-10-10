using KnitErp.Application.Access;
using KnitErp.Application.Catalog;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Domain.Organizations;
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

            // Динамика на главной: продажи за день = отгружено 61 000 − возвращено 4 880; поступления — 40 000.
            var charts = (await s.Dashboard.GetAsync()).Charts;
            var sales = charts.Single(c => c.Title == "Продажи");
            Assert.Equal((DashboardService.ChartDays, Day, 56_120m, 56_120m, 0m), (sales.Points.Count, sales.From.AddDays(sales.Points.Count - 1), sales.Points[^1], sales.Total, sales.PreviousTotal));
            Assert.Equal(40_000m, charts.Single(c => c.Title == "Поступления от покупателей").Total);
            Assert.Equal(0m, charts.Single(c => c.Title == "Закупки").Total);
        }

        await using (var s = host.As(f.Senior, f.Org.OrganizationId))
        {
            var dto = await s.Sales.GetOrderAsync(order);
            Assert.Null(dto.Total);
            Assert.False(dto.CanEdit);
            Assert.Empty((await s.Dashboard.GetAsync()).Charts);
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

    [SqlFact]
    public async Task Invoice_print_forms_and_supplier_invoices()
    {
        var f = await SetUpAsync();
        long order, invoice, ship;
        await using (var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId))
        {
            long? reason;
            await using (var db = host.NewDb())
            {
                reason = await db.OperationReasons.Where(r => r.OrganizationId == f.Org.OrganizationId && r.Kind == StockOperationKind.Receipt)
                    .Select(r => (long?)r.Id).FirstAsync();
            }

            var receipt = await s.Documents.CreateAsync(StockOperationKind.Receipt, new StockDocumentHeader(f.Store, null, null, reason, Day, null));
            await s.Documents.SetLineAsync(receipt, f.Sweater, 30, (await s.Documents.GetAsync(receipt)).RowVersion);
            await s.Documents.PostAsync(receipt, (await s.Documents.GetAsync(receipt)).RowVersion);

            order = await s.Sales.CreateOrderAsync(new SalesOrderHeader(Day, f.Customer, f.Store, null, "Договор № 7 от 01.10.2026", false, null));
            await s.Sales.SetOrderLineAsync(order, f.Sweater, 25, 2000m, 22m, (await s.Sales.GetOrderAsync(order)).RowVersion);
            Assert.Equal("sales.invoice.order_status",
                (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Invoices.CreateAsync(order, null, null))).Code);
            await s.Sales.ConfirmOrderAsync(order, (await s.Sales.GetOrderAsync(order)).RowVersion);

            // Счёт по заказу: строки и суммы — из заказа; второй действующий счёт по заказу не выставить.
            Assert.Equal("sales.invoice.due_date",
                (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Invoices.CreateAsync(order, Day.AddDays(-1), null))).Code);
            invoice = await s.Invoices.CreateAsync(order, Day.AddDays(5), null);
            var dto = await s.Invoices.GetAsync(invoice);
            Assert.StartsWith("СЧ-", dto.Number);
            Assert.Equal((61_000m, 11_000m, 0m, InvoicePaymentState.Unpaid), (dto.Total, dto.VatTotal, dto.Paid, dto.PaymentState));
            Assert.Equal(invoice, await s.Invoices.FindForOrderAsync(order));
            Assert.Equal("sales.invoice.exists", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Invoices.CreateAsync(order, null, null))).Code);

            await s.Sales.CreatePaymentAsync(Day, f.Customer, order, 40_000m, null);
            var row = (await s.Invoices.ListAsync(new CustomerInvoiceFilter())).Single(r => r.Id == invoice);
            Assert.Equal((40_000m, InvoicePaymentState.Partial, false), (row.Paid, row.PaymentState, row.Overdue));

            // Реквизиты для печати: БИК и счета проверяются по контрольному ключу.
            var org = await s.Organizations.GetCurrentAsync();
            Assert.Equal("org.bank.bik", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Organizations.UpdatePrintRequisitesAsync(
                new UpdatePrintRequisitesCommand(new PrintRequisites(null, "Банк", "12345", null, null, null, null), org.RowVersion)))).Code);
            Assert.Equal("org.bank.account", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Organizations.UpdatePrintRequisitesAsync(
                new UpdatePrintRequisitesCommand(new PrintRequisites(null, "Банк", "044525225", "40702810138000000000", null, null, null), org.RowVersion)))).Code);
            org = await s.Organizations.UpdatePrintRequisitesAsync(new UpdatePrintRequisitesCommand(new PrintRequisites(
                "г. Москва, ул. Тестовая, д. 1", "ПАО «Тестбанк»", "044525225", "4070 2810 9380 0000 0001", "30101810400000000225", "Иванов И. И.", null),
                org.RowVersion));
            Assert.Equal(("40702810938000000001", "Иванов И. И."), (org.PrintRequisites.BankAccount, org.PrintRequisites.DirectorName));

            var cp = (await s.Counterparties.ListAsync(new CounterpartyFilter())).Counterparties.Single(c => c.Id == f.Customer);
            await s.Counterparties.UpdateAsync(f.Customer, new CounterpartyCommand(cp.Name, null, null, false, true, null, Address: "г. Тверь, пр. Ленина, 5"),
                cp.RowVersion);
            cp = (await s.Counterparties.ListAsync(new CounterpartyFilter())).Counterparties.Single(c => c.Id == f.Customer);
            await s.Counterparties.UpdateAsync(f.Customer, new CounterpartyCommand(cp.Name, null, null, false, true, "без адреса в команде"), cp.RowVersion);

            var print = await s.Print.InvoiceAsync(invoice);
            Assert.Equal("Шестьдесят одна тысяча рублей 00 копеек", print.TotalInWords);
            Assert.Equal(("044525225", "ООО «Магазин»", "г. Тверь, пр. Ленина, 5"), (print.Requisites.BankBic, print.Buyer.Name, print.Buyer.Address));
            Assert.Equal("Договор № 7 от 01.10.2026; заказ " + (await s.Sales.GetOrderAsync(order)).Number + " от 09.10.2026", print.Basis);
            var line = print.Lines.Single();
            Assert.Equal((50_000m, 11_000m, 61_000m, "796"), (line.AmountWithoutVat, line.VatAmount, line.Amount, line.UnitCode));

            // УПД — только по проведённой отгрузке; цены — из заказа.
            ship = await s.Sales.CreateShipmentAsync(order);
            await s.Documents.SetLineAsync(ship, f.Sweater, 10, (await s.Documents.GetAsync(ship)).RowVersion);
            Assert.Equal("print.upd.document", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Print.UpdAsync(ship))).Code);
            await s.Documents.PostAsync(ship, (await s.Documents.GetAsync(ship)).RowVersion);
            var upd = await s.Print.UpdAsync(ship);
            Assert.Equal((20_000m, 4_400m, 24_400m), (upd.TotalWithoutVat, upd.VatTotal, upd.Total));
            Assert.Equal((2_000m, "Двадцать четыре тысячи четыреста рублей 00 копеек"), (upd.Lines.Single().Price, upd.TotalInWords));
            Assert.EndsWith("643", upd.Currency);

            // Отмена счёта с причиной — затем можно выставить новый.
            Assert.Equal("field.required", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Invoices.CancelAsync(invoice, " ", dto.RowVersion))).Code);
            await s.Invoices.CancelAsync(invoice, "Покупатель просит другой срок", (await s.Invoices.GetAsync(invoice)).RowVersion);
            Assert.True((await s.Print.InvoiceAsync(invoice)).Cancelled);
            Assert.Empty(await s.Invoices.ListAsync(new CustomerInvoiceFilter()));
            Assert.Single(await s.Invoices.ListAsync(new CustomerInvoiceFilter(IncludeCancelled: true)));
            Assert.NotEqual(invoice, await s.Invoices.CreateAsync(order, null, null));

            // Счёт поставщика — номер в шапке заказа поставщику; оплачено — по оплатам заказа.
            var supplier = await s.Counterparties.CreateAsync(new CounterpartyCommand("ООО «Пряжа»", null, null, true, false, null));
            var po = await s.Purchases.CreateOrderAsync(new KnitErp.Domain.Purchasing.PurchaseOrderHeader(Day, supplier, f.Store, null, "№ 15 от 05.10.2026", true, null));
            await s.Purchases.SetOrderLineAsync(po, f.Sweater, 10, 500m, 22m, (await s.Purchases.GetOrderAsync(po)).RowVersion);
            Assert.Empty(await s.Purchases.ListSupplierInvoicesAsync());
            await s.Purchases.ConfirmOrderAsync(po, (await s.Purchases.GetOrderAsync(po)).RowVersion);
            await s.Purchases.CreatePaymentAsync(Day, supplier, po, 1_000m, null);
            var si = (await s.Purchases.ListSupplierInvoicesAsync("№ 15")).Single();
            Assert.Equal((5_000m, 1_000m, 4_000m), (si.Total, si.Paid, si.ToPay));
        }

        await using (var s = host.As(f.Senior, f.Org.OrganizationId))
        {
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Invoices.ListAsync(new CustomerInvoiceFilter()));
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Print.UpdAsync(ship));
        }

        var other = await CreateOrgAsync();
        await using (var s = host.As(other.OwnerUserId, other.OrganizationId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => s.Invoices.GetAsync(invoice));
            await Assert.ThrowsAsync<NotFoundException>(() => s.Print.InvoiceAsync(invoice));
            await Assert.ThrowsAsync<NotFoundException>(() => s.Print.UpdAsync(ship));
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
