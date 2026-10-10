using KnitErp.Application.Catalog;
using KnitErp.Domain.Audit;
using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Application.Finance;
using KnitErp.Application.Organizations;
using KnitErp.Application.Purchasing;
using KnitErp.Application.Warehousing;
using KnitErp.Domain.Access;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Finance;
using KnitErp.Domain.Organizations;
using KnitErp.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>D84: кассовые ордера, поступления и списания по счёту, перемещение между своими счетами; юрлицо-покупатель в закупке.</summary>
public sealed class MoneyOperationTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private static int _innSeed = 830_000_000;
    private static readonly DateOnly Day = new(2026, 10, 9);

    [SqlFact]
    public async Task Cash_orders_transfers_and_balances()
    {
        var org = await CreateOrgAsync();
        await using var s = host.As(org.OwnerUserId, org.OrganizationId);
        var ooo = (await s.LegalEntities.ListAsync()).Single();
        var bank = await s.LegalEntities.AddAccountAsync(ooo.Id, new LegalEntityAccountCommand("ПАО «Тестбанк»", "044525225", "40702810938000000001", null));
        var cash = await s.LegalEntities.AddCashAsync(ooo.Id, "Касса цеха");

        // Приход в кассу — ПКО, выдача — РКО, по счёту — ПБ/СБ, перемещение — ПД; номера сквозные по префиксу.
        var income = await s.MoneyOperations.CreateAsync(new MoneyOperationCommand(Day, MoneyOperationKind.Income, cash, null, 1_000m,
            "Иванов И. И.", "Возврат подотчётной суммы", null));
        var op = await s.MoneyOperations.GetAsync(income);
        Assert.StartsWith("ПКО", op.Number);
        Assert.Equal((true, "Приходный кассовый ордер"), (op.Cash, op.KindName));

        // Касса не уходит в минус: выдать 1 500 при остатке 1 000 нельзя.
        Assert.Equal("money.cash.insufficient", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.MoneyOperations.CreateAsync(
            new MoneyOperationCommand(Day, MoneyOperationKind.Expense, cash, null, 1_500m, "Петров П. П.", "Под отчёт", null)))).Code);
        var expense = await s.MoneyOperations.CreateAsync(new MoneyOperationCommand(Day, MoneyOperationKind.Expense, cash, null, 300m,
            "Петров П. П.", "Под отчёт на хозяйственные нужды", null));
        Assert.StartsWith("РКО", (await s.MoneyOperations.GetAsync(expense)).Number);

        // Сдача наличных в банк — перемещение касса → счёт; поступление на счёт — ПБ.
        var transfer = await s.MoneyOperations.CreateAsync(new MoneyOperationCommand(Day, MoneyOperationKind.Transfer, cash, bank, 500m,
            null, "Сдача наличных в банк", null));
        Assert.StartsWith("ПД", (await s.MoneyOperations.GetAsync(transfer)).Number);
        var bankIn = await s.MoneyOperations.CreateAsync(new MoneyOperationCommand(Day, MoneyOperationKind.Income, bank, null, 2_000m,
            "АО «Страховая»", "Страховое возмещение", null));
        Assert.StartsWith("ПБ", (await s.MoneyOperations.GetAsync(bankIn)).Number);

        var balances = await s.Money.BalancesAsync();
        Assert.Equal(200m, balances.Single(b => b.AccountId == cash).Balance);
        Assert.Equal(2_500m, balances.Single(b => b.AccountId == bank).Balance);

        // Неверные данные: перемещение в тот же счёт, без суммы, без основания.
        await Assert.ThrowsAsync<BusinessRuleException>(() => s.MoneyOperations.CreateAsync(
            new MoneyOperationCommand(Day, MoneyOperationKind.Transfer, bank, bank, 1m, null, "x", null)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => s.MoneyOperations.CreateAsync(
            new MoneyOperationCommand(Day, MoneyOperationKind.Income, bank, null, 0m, null, "x", null)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => s.MoneyOperations.CreateAsync(
            new MoneyOperationCommand(Day, MoneyOperationKind.Income, bank, null, 1m, null, " ", null)));

        // Перемещение между юрлицами запрещено — это заём или оплата.
        var ip = await s.LegalEntities.CreateAsync(new LegalEntityData(LegalEntityKind.SoleProprietor,
            "Индивидуальный предприниматель Петров Пётр Петрович", "ИП Петров П. П.", "500100732259", null, "304500116000157",
            null, null, "Петров П. П.", null, VatExempt: true));
        var ipCash = await s.LegalEntities.AddCashAsync(ip, "Касса ИП");
        Assert.Equal("money.transfer.entity", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.MoneyOperations.CreateAsync(
            new MoneyOperationCommand(Day, MoneyOperationKind.Transfer, bank, ipCash, 10m, null, "x", null)))).Code);

        // Отмена прихода, после которого из кассы уже выдали, — нельзя: касса ушла бы в минус (200 − 1 000).
        var incomeRow = await s.MoneyOperations.GetAsync(income);
        Assert.Equal("money.cash.insufficient", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.MoneyOperations.CancelAsync(income, "ошибка", incomeRow.RowVersion))).Code);

        // Отмена выдачи — касса вернулась; повторная отмена со старой версией — конфликт.
        var expenseRow = await s.MoneyOperations.GetAsync(expense);
        await s.MoneyOperations.CancelAsync(expense, "выдали по ошибке", expenseRow.RowVersion);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => s.MoneyOperations.CancelAsync(expense, "ещё раз", expenseRow.RowVersion));
        Assert.Equal(500m, (await s.Money.BalancesAsync()).Single(b => b.AccountId == cash).Balance);
        Assert.Equal(MoneyOperationStatus.Cancelled, (await s.MoneyOperations.GetAsync(expense)).Status);
        Assert.Equal(3, (await s.MoneyOperations.ListAsync(new MoneyOperationFilter(IncludeCancelled: false))).Count);
        Assert.Equal(3, (await s.MoneyOperations.ListAsync(new MoneyOperationFilter(AccountId: cash))).Count);

        // Печать: КО-1 для прихода в кассу с суммой прописью; для перемещения и операций по счёту — нет.
        var ko1 = await s.MoneyOperations.CashOrderAsync(income);
        Assert.False(ko1.Expense);
        Assert.Equal(("Иванов И. И.", "Касса цеха", 1_000m), (ko1.Party, ko1.Cashbox, ko1.Amount));
        Assert.StartsWith("Одна тысяча", ko1.AmountInWords, StringComparison.OrdinalIgnoreCase);
        Assert.True((await s.MoneyOperations.CashOrderAsync(expense)).Cancelled);
        // D86: сдача наличных в банк — РКО кассы в общей нумерации (после РКО-000001 выдачи); стороны банка ордера нет.
        var rko = await s.MoneyOperations.CashOrderAsync(transfer);
        Assert.Equal((true, "РКО-000002", 500m), (rko.Expense, rko.Number, rko.Amount));
        Assert.Equal("money.print.not_cash", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.MoneyOperations.CashOrderAsync(transfer, true))).Code);
        Assert.Equal("money.print.not_cash", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.MoneyOperations.CashOrderAsync(bankIn))).Code);

        // Аудит: создание и отмена.
        Assert.True(await s.Db.AuditEntries.AnyAsync(a => a.OrganizationId == org.OrganizationId && a.Action == AuditActions.MoneyOperationCancelled
                                                         && a.EntityId == expense.ToString()));

        // Закрытый период: задним числом не провести.
        await s.Period.CloseAsync(Day, null);
        Assert.Equal("period.closed", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.MoneyOperations.CreateAsync(
            new MoneyOperationCommand(Day, MoneyOperationKind.Income, bank, null, 1m, null, "x", null)))).Code);

        // Чужая организация: операции и счета — «не найдено».
        var other = await CreateOrgAsync();
        await using var o = host.As(other.OwnerUserId, other.OrganizationId);
        await Assert.ThrowsAsync<NotFoundException>(() => o.MoneyOperations.GetAsync(income));
        await Assert.ThrowsAsync<NotFoundException>(() => o.MoneyOperations.CashOrderAsync(income));
        await Assert.ThrowsAsync<NotFoundException>(() => o.MoneyOperations.CreateAsync(
            new MoneyOperationCommand(Day.AddDays(1), MoneyOperationKind.Income, cash, null, 1m, null, "x", null)));
        Assert.Empty(await o.MoneyOperations.ListAsync(new MoneyOperationFilter()));
    }

    [SqlFact]
    public async Task Auditor_sees_operations_but_cannot_post_storekeeper_sees_nothing()
    {
        var org = await CreateOrgAsync();
        long cash, auditor, storekeeper;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            cash = await s.LegalEntities.AddCashAsync((await s.LegalEntities.ListAsync()).Single().Id, "Касса");
            await s.MoneyOperations.CreateAsync(new MoneyOperationCommand(Day, MoneyOperationKind.Income, cash, null, 100m, null, "Размен", null));
            var store = await s.Warehouses.CreateWarehouseAsync("Склад", null);
            auditor = (await s.Access.InviteAsync(new InviteUserCommand($"aud-{Guid.NewGuid():N}@test.local", "Аудитор", SystemRoles.Auditor, null))).UserId;
            storekeeper = (await s.Access.InviteAsync(new InviteUserCommand($"sk-{Guid.NewGuid():N}@test.local", "Кладовщик", SystemRoles.Storekeeper, null,
                WarehouseId: store))).UserId;
        }

        await using (var db = host.NewDb())
        {
            foreach (var u in await db.Users.Where(u => u.Id == auditor || u.Id == storekeeper).ToListAsync())
            {
                u.Activate();
            }

            await db.SaveChangesAsync();
        }

        await using (var a = host.As(auditor, org.OrganizationId))
        {
            Assert.Single(await a.MoneyOperations.ListAsync(new MoneyOperationFilter()));
            await Assert.ThrowsAsync<AccessDeniedException>(() => a.MoneyOperations.CreateAsync(
                new MoneyOperationCommand(Day, MoneyOperationKind.Income, cash, null, 1m, null, "x", null)));
        }

        await using (var k = host.As(storekeeper, org.OrganizationId))
        {
            await Assert.ThrowsAsync<AccessDeniedException>(() => k.MoneyOperations.ListAsync(new MoneyOperationFilter()));
        }
    }

    [SqlFact]
    public async Task Purchase_order_buyer_entity_restricts_payment_account_and_vat_journal()
    {
        var org = await CreateOrgAsync();
        await using var s = host.As(org.OwnerUserId, org.OrganizationId);
        var ooo = (await s.LegalEntities.ListAsync()).Single();
        var oooCash = await s.LegalEntities.AddCashAsync(ooo.Id, "Касса ООО");
        var ip = await s.LegalEntities.CreateAsync(new LegalEntityData(LegalEntityKind.SoleProprietor,
            "Индивидуальный предприниматель Петров Пётр Петрович", "ИП Петров П. П.", "500100732259", null, "304500116000157",
            null, null, "Петров П. П.", null, VatExempt: true));
        var ipCash = await s.LegalEntities.AddCashAsync(ip, "Касса ИП");
        var units = await s.Catalog.ListUnitsAsync();
        var store = await s.Warehouses.CreateWarehouseAsync("Склад", null);
        var wool = await s.Catalog.CreateItemAsync(new ItemCommand("ПР-1", "Пряжа", ItemType.RawMaterial, units.Single(u => u.Symbol == "кг").Id, null));
        var supplier = await s.Counterparties.CreateAsync(new CounterpartyCommand("ООО «Пряжа»", null, null, true, false, null));

        // Без юрлица — основное; список выбора — оба юрлица.
        var po = await s.Purchases.CreateOrderAsync(new PurchaseOrderHeader(Day, supplier, store, null, "№ 5", true, null));
        Assert.Equal(ooo.Id, (await s.Purchases.GetOrderAsync(po)).LegalEntityId);
        Assert.Equal(2, (await s.Purchases.GetOptionsAsync()).LegalEntities!.Count);

        // Смена в черновике на ИП; чужое юрлицо — «не найдено».
        var other = await CreateOrgAsync();
        long otherEntity;
        await using (var o = host.As(other.OwnerUserId, other.OrganizationId))
        {
            otherEntity = (await o.LegalEntities.ListAsync()).Single().Id;
        }

        await Assert.ThrowsAsync<NotFoundException>(async () => await s.Purchases.UpdateOrderHeaderAsync(po,
            new PurchaseOrderHeader(Day, supplier, store, null, "№ 5", true, null, otherEntity), (await s.Purchases.GetOrderAsync(po)).RowVersion));
        await s.Purchases.UpdateOrderHeaderAsync(po, new PurchaseOrderHeader(Day, supplier, store, null, "№ 5", true, null, ip),
            (await s.Purchases.GetOrderAsync(po)).RowVersion);
        var order = await s.Purchases.GetOrderAsync(po);
        Assert.Equal((ip, "ИП Петров П. П."), (order.LegalEntityId, order.LegalEntity));

        // Без юрлица в правке шапки — юрлицо заказа не меняется.
        await s.Purchases.UpdateOrderHeaderAsync(po, new PurchaseOrderHeader(Day, supplier, store, null, "№ 5", true, "правка", null), order.RowVersion);
        Assert.Equal(ip, (await s.Purchases.GetOrderAsync(po)).LegalEntityId);

        await s.Purchases.SetOrderLineAsync(po, wool, 2, 100m, null, (await s.Purchases.GetOrderAsync(po)).RowVersion);
        await s.Purchases.ConfirmOrderAsync(po, (await s.Purchases.GetOrderAsync(po)).RowVersion);

        // Оплата по заказу ИП — только из счёта или кассы ИП; касса ИП пуста — в минус нельзя.
        Assert.Equal("money.account_entity", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Purchases.CreatePaymentAsync(Day, supplier, po, 50m, null, oooCash))).Code);
        Assert.Equal("money.cash.insufficient", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Purchases.CreatePaymentAsync(Day, supplier, po, 50m, null, ipCash))).Code);
        await s.MoneyOperations.CreateAsync(new MoneyOperationCommand(Day, MoneyOperationKind.Income, ipCash, null, 100m, "Петров П. П.", "Взнос наличных", null));
        await s.Purchases.CreatePaymentAsync(Day, supplier, po, 50m, null, ipCash);
        Assert.Equal(50m, (await s.Money.BalancesAsync()).Single(b => b.AccountId == ipCash).Balance);

        // Журнал полученных счетов-фактур: покупатель — юрлицо заказа, фильтр по юрлицу.
        var receipt = await s.Purchases.CreateReceiptAsync(po);
        await s.Documents.PostAsync(receipt, (await s.Documents.GetAsync(receipt)).RowVersion);
        await s.VatInvoices.RegisterAsync(new KnitErp.Application.Taxes.RegisterVatInvoiceCommand(receipt, "5", Day, 200m, 0m, null));
        var period = new KnitErp.Application.Taxes.VatJournalFilter(Day.AddDays(-1), Day.AddDays(1));
        Assert.Equal("ИП Петров П. П.", (await s.VatInvoices.ReceivedAsync(period)).Single().Buyer);
        Assert.Single(await s.VatInvoices.ReceivedAsync(period with { LegalEntityId = ip }));
        Assert.Empty(await s.VatInvoices.ReceivedAsync(period with { LegalEntityId = ooo.Id }));
        Assert.Contains(await s.VatInvoices.BuyersAsync(), b => b.Id == ip);
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
