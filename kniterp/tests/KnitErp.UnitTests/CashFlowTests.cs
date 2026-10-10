using KnitErp.Domain.Common;
using KnitErp.Domain.Finance;

namespace KnitErp.UnitTests;

/// <summary>D86: статьи ДДС, авансовый отчёт, перемещение без статьи.</summary>
public sealed class CashFlowTests
{
    [Fact]
    public void Defaults_have_one_item_per_system_code_and_system_items_are_locked()
    {
        var codes = CashFlowItem.Defaults.Where(d => d.Code is not null).Select(d => d.Code).ToList();
        Assert.Equal(codes.Distinct().Count(), codes.Count);
        Assert.Contains(CashFlowItem.CustomerPayments, codes);
        Assert.Contains(CashFlowItem.AccountableIssue, codes);
        Assert.Equal(CashFlowItem.Defaults.Count, CashFlowItem.Defaults.Select(d => (d.Direction, d.Name)).Distinct().Count());

        var system = CashFlowItem.Create(1, CashFlowDirection.Out, "Прочие выплаты", CashFlowItem.OtherExpense);
        Assert.Equal("cash_flow_item.system", Assert.Throws<BusinessRuleException>(() => system.Rename("Другое")).Code);
        Assert.Equal("cash_flow_item.system", Assert.Throws<BusinessRuleException>(() => system.SetArchived(true)).Code);
        var own = CashFlowItem.Create(1, CashFlowDirection.Out, "  Ремонт  ");
        Assert.Equal("Ремонт", own.Name);
        own.SetArchived(true);
        Assert.True(own.IsArchived);
    }

    [Fact]
    public void Expense_report_is_edited_as_draft_approved_once_and_cancelled_with_reason()
    {
        var day = new DateOnly(2026, 10, 9);
        var r = ExpenseReport.Create(1, "АО-000001", day, 5, 7, " Командировка ", 1, DateTime.UtcNow);
        Assert.Equal("Командировка", r.Purpose);
        Assert.Equal("expense_report.empty", Assert.Throws<BusinessRuleException>(() => r.Approve(1, DateTime.UtcNow)).Code);
        Assert.Equal("expense_report.amount", Assert.Throws<BusinessRuleException>(() => r.AddLine(day, "Чек № 1", null, 0m, 3)).Code);
        r.AddLine(day, "Чек № 1", "Бензин", 1_200.50m, 3);
        r.AddLine(day.AddDays(1), "Билет", null, 800m, 3);
        Assert.Equal(2_000.50m, r.Total);
        Assert.Equal(2, r.LinesRevision);
        Assert.Equal("expense_report.document_date", Assert.Throws<BusinessRuleException>(() => r.Approve(1, DateTime.UtcNow)).Code);

        r.UpdateHeader(day.AddDays(1), null);
        r.Approve(1, DateTime.UtcNow);
        Assert.Equal(ExpenseReportStatus.Approved, r.Status);
        Assert.Equal("expense_report.not_draft", Assert.Throws<BusinessRuleException>(() => r.AddLine(day, "x", null, 1m, 3)).Code);
        Assert.Throws<BusinessRuleException>(() => r.Cancel(1, " ", DateTime.UtcNow));
        r.Cancel(1, "чеки не те", DateTime.UtcNow);
        Assert.Equal("expense_report.cancelled", Assert.Throws<BusinessRuleException>(() => r.Cancel(1, "ещё", DateTime.UtcNow)).Code);
    }

    [Fact]
    public void Transfer_has_no_cash_flow_item_and_only_transfers_get_cash_orders()
    {
        Assert.Equal("money.transfer.item", Assert.Throws<BusinessRuleException>(() => MoneyOperation.Create(1, "ПД-1", new DateOnly(2026, 10, 9),
            MoneyOperationKind.Transfer, 1, 2, 10m, null, "Сдача в банк", null, 1, DateTime.UtcNow, cashFlowItemId: 5)).Code);
        var income = MoneyOperation.Create(1, "ПКО-1", new DateOnly(2026, 10, 9), MoneyOperationKind.Income, 1, null, 10m, null, "Взнос", null, 1,
            DateTime.UtcNow, 5);
        Assert.Throws<InvalidOperationException>(() => income.AssignTransferCashOrders("РКО-1", null));
    }
}
