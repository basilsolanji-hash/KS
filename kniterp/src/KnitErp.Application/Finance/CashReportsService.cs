using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Common;
using KnitErp.Domain.Finance;
using KnitErp.Domain.Organizations;
using KnitErp.Domain.Purchasing;
using KnitErp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Finance;

/// <summary>Строка ДДС: статья и сумма за период.</summary>
public sealed record CashFlowLineDto(long ItemId, string Item, CashFlowDirection Direction, decimal Amount);

/// <summary>
/// Отчёт о движении денег за период. OpeningEntered — начальные остатки счетов, введённые с датой внутри периода
/// (деньги появились в учёте, но не поступили). Closing = Opening + OpeningEntered + In − Out.
/// </summary>
public sealed record CashFlowReportDto(DateOnly From, DateOnly To, long? LegalEntityId, decimal Opening, decimal OpeningEntered,
    IReadOnlyList<CashFlowLineDto> Lines, decimal Closing)
{
    public decimal In => Lines.Where(l => l.Direction == CashFlowDirection.In).Sum(l => l.Amount);
    public decimal Out => Lines.Where(l => l.Direction == CashFlowDirection.Out).Sum(l => l.Amount);
    public decimal Net => In - Out;
}

/// <summary>Строка кассовой книги: номер ордера, от кого получено или кому выдано, приход или расход.</summary>
public sealed record CashBookLineDto(string Number, string Party, decimal In, decimal Out);

/// <summary>Лист кассовой книги за день (вкладной лист и отчёт кассира КО-4). SheetNumber — номер листа с начала года.</summary>
public sealed record CashBookSheetDto(DateOnly Date, int SheetNumber, decimal Opening, IReadOnlyList<CashBookLineDto> Lines)
{
    public decimal In => Lines.Sum(l => l.In);
    public decimal Out => Lines.Sum(l => l.Out);
    public decimal Closing => Opening + In - Out;
    public int IncomeOrders => Lines.Count(l => l.In > 0);
    public int ExpenseOrders => Lines.Count(l => l.Out > 0);
}

public sealed record CashBookDto(
    long AccountId, string Cashbox, string Organization, string? Inn, string? Kpp, bool SoleProprietor, string? AccountantName, string? DirectorName,
    DateOnly From, DateOnly To, IReadOnlyList<CashBookSheetDto> Sheets);

/// <summary>
/// Отчёты по деньгам (D86): движение денежных средств по статьям за период и кассовая книга (КО-4, ОКУД 0310004, постановление
/// Госкомстата России № 88; ведение — п. 4.6 Указания Банка России № 3210-У). Источники те же, что в остатках по счетам: проведённые
/// оплаты покупателей и поставщикам, денежные операции, начальные остатки; движения до даты начального остатка счёта не учитываются.
/// Право — «Цены и суммы».
/// </summary>
public sealed class CashReportsService(IKnitErpDbContext db, IAccessGuard guard)
{
    public const int MaxDays = 3 * 366;

    private sealed record Movement(long? AccountId, DateOnly Date, long ItemId, CashFlowDirection Direction, decimal Amount);

    public async Task<CashFlowReportDto> CashFlowAsync(DateOnly from, DateOnly to, long? legalEntityId = null, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.PriceView, ct);
        EnsurePeriod(from, to, MaxDays);
        var org = ctx.OrganizationId;
        if (legalEntityId is { } eid && !await db.LegalEntities.AnyAsync(e => e.Id == eid && e.OrganizationId == org, ct))
        {
            throw new NotFoundException("Юрлицо");
        }

        var accounts = await db.LegalEntityAccounts.AsNoTracking().Where(a => a.OrganizationId == org)
            .Select(a => new { a.Id, a.LegalEntityId, a.OpeningDate, a.OpeningBalance }).ToDictionaryAsync(a => a.Id, ct);
        bool InScope(long? account) => account is { } id ? legalEntityId is null || accounts[id].LegalEntityId == legalEntityId : legalEntityId is null;
        bool Counts(long? account, DateOnly day) => account is not { } id || accounts[id].OpeningDate is not { } d || day >= d;

        var items = await db.CashFlowItems.AsNoTracking().Where(i => i.OrganizationId == org).ToDictionaryAsync(i => i.Id, ct);
        long Code(string code) => items.Values.Single(i => i.SystemCode == code).Id;
        var customerItem = Code(CashFlowItem.CustomerPayments);
        var supplierItem = Code(CashFlowItem.SupplierPayments);
        var otherIn = Code(CashFlowItem.OtherIncome);
        var otherOut = Code(CashFlowItem.OtherExpense);

        var movements = new List<Movement>();
        movements.AddRange((await db.CustomerPayments.AsNoTracking()
                .Where(p => p.OrganizationId == org && p.Status == CustomerPaymentStatus.Posted && p.PaymentDate >= from && p.PaymentDate <= to)
                .Select(p => new { p.MoneyAccountId, p.PaymentDate, p.Amount }).ToListAsync(ct))
            .Select(p => new Movement(p.MoneyAccountId, p.PaymentDate, customerItem, CashFlowDirection.In, p.Amount)));
        movements.AddRange((await db.SupplierPayments.AsNoTracking()
                .Where(p => p.OrganizationId == org && p.Status == SupplierPaymentStatus.Posted && p.PaymentDate >= from && p.PaymentDate <= to)
                .Select(p => new { p.MoneyAccountId, p.PaymentDate, p.Amount }).ToListAsync(ct))
            .Select(p => new Movement(p.MoneyAccountId, p.PaymentDate, supplierItem, CashFlowDirection.Out, p.Amount)));
        movements.AddRange((await db.MoneyOperations.AsNoTracking()
                .Where(o => o.OrganizationId == org && o.Status == MoneyOperationStatus.Posted && o.Kind != MoneyOperationKind.Transfer
                            && o.OperationDate >= from && o.OperationDate <= to)
                .Select(o => new { o.AccountId, o.OperationDate, o.Kind, o.CashFlowItemId, o.Amount }).ToListAsync(ct))
            .Select(o => o.Kind == MoneyOperationKind.Income
                ? new Movement(o.AccountId, o.OperationDate, o.CashFlowItemId ?? otherIn, CashFlowDirection.In, o.Amount)
                : new Movement(o.AccountId, o.OperationDate, o.CashFlowItemId ?? otherOut, CashFlowDirection.Out, o.Amount)));

        // Перемещения между своими счетами — внутри одного юрлица, поэтому в ДДС не попадают ни при каком отборе.
        var lines = movements.Where(m => InScope(m.AccountId) && Counts(m.AccountId, m.Date))
            .GroupBy(m => (m.ItemId, m.Direction))
            .Select(g => new CashFlowLineDto(g.Key.ItemId, items[g.Key.ItemId].Name, g.Key.Direction, g.Sum(m => m.Amount)))
            .OrderBy(l => l.Direction).ThenByDescending(l => l.Amount).ThenBy(l => l.Item).ToList();

        var before = await MoneyService.BalancesAsync(db, org, from.AddDays(-1), includeArchived: true, ct);
        var after = await MoneyService.BalancesAsync(db, org, to, includeArchived: true, ct);
        decimal Total(IEnumerable<MoneyAccountBalanceDto> rows) => rows.Where(b => InScope(b.AccountId)).Sum(b => b.Balance);
        var entered = accounts.Values.Where(a => InScope(a.Id) && a.OpeningDate is { } d && d >= from && d <= to).Sum(a => a.OpeningBalance);
        return new CashFlowReportDto(from, to, legalEntityId, Total(before), entered, lines, Total(after));
    }

    public async Task<CashBookDto> CashBookAsync(long cashAccountId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.PriceView, ct);
        EnsurePeriod(from, to, 366);
        var org = ctx.OrganizationId;
        var cash = await db.LegalEntityAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == cashAccountId && a.OrganizationId == org, ct)
                   ?? throw new NotFoundException("Касса");
        if (!cash.IsCash)
        {
            throw new BusinessRuleException("cash_book.not_cash", "Кассовая книга ведётся по кассе, а не по расчётному счёту.");
        }

        var entity = await db.LegalEntities.AsNoTracking().SingleAsync(e => e.Id == cash.LegalEntityId, ct);
        var labels = (await db.LegalEntityAccounts.AsNoTracking().Where(a => a.OrganizationId == org)
                .Select(a => new { a.Id, a.Kind, a.BankName, a.Account }).ToListAsync(ct))
            .ToDictionary(a => a.Id, a => MoneyService.Label(a.Kind, a.BankName, a.Account));

        // Все движения кассы до конца периода: остаток на начало дня и номера листов с начала года считаются по ним.
        var moves = new List<(DateOnly Date, long Order, CashBookLineDto Line)>();
        var ops = await db.MoneyOperations.AsNoTracking()
            .Where(o => o.OrganizationId == org && o.Status == MoneyOperationStatus.Posted && o.OperationDate <= to
                        && (o.AccountId == cash.Id || o.TargetAccountId == cash.Id))
            .ToListAsync(ct);
        foreach (var o in ops)
        {
            if (o.Kind == MoneyOperationKind.Transfer)
            {
                moves.Add(o.AccountId == cash.Id
                    ? (o.OperationDate, o.Id, new CashBookLineDto(o.CashOrderNumber ?? o.Number, $"{o.Basis} ({labels[o.TargetAccountId!.Value]})", 0, o.Amount))
                    : (o.OperationDate, o.Id, new CashBookLineDto(o.TargetCashOrderNumber ?? o.Number, $"{o.Basis} ({labels[o.AccountId]})", o.Amount, 0)));
            }
            else
            {
                var party = o.Party ?? o.Basis;
                moves.Add((o.OperationDate, o.Id, o.Kind == MoneyOperationKind.Income
                    ? new CashBookLineDto(o.Number, party, o.Amount, 0)
                    : new CashBookLineDto(o.Number, party, 0, o.Amount)));
            }
        }

        moves.AddRange((await (from p in db.CustomerPayments.AsNoTracking()
                               where p.OrganizationId == org && p.MoneyAccountId == cash.Id && p.Status == CustomerPaymentStatus.Posted && p.PaymentDate <= to
                               join c in db.Counterparties.AsNoTracking() on p.CustomerId equals c.Id
                               select new { p.Id, p.PaymentDate, Number = p.CashOrderNumber ?? p.Number, c.Name, p.Amount }).ToListAsync(ct))
            .Select(p => (p.PaymentDate, p.Id, new CashBookLineDto(p.Number, p.Name, p.Amount, 0))));
        moves.AddRange((await (from p in db.SupplierPayments.AsNoTracking()
                               where p.OrganizationId == org && p.MoneyAccountId == cash.Id && p.Status == SupplierPaymentStatus.Posted && p.PaymentDate <= to
                               join c in db.Counterparties.AsNoTracking() on p.SupplierId equals c.Id
                               select new { p.Id, p.PaymentDate, Number = p.CashOrderNumber ?? p.Number, c.Name, p.Amount }).ToListAsync(ct))
            .Select(p => (p.PaymentDate, p.Id, new CashBookLineDto(p.Number, p.Name, 0, p.Amount))));

        var counted = moves.Where(m => cash.OpeningDate is not { } d || m.Date >= d).ToList();
        var days = counted.Select(m => m.Date).Distinct().OrderBy(d => d).ToList();
        var sheets = new List<CashBookSheetDto>();
        foreach (var day in days.Where(d => d >= from))
        {
            var opening = (cash.OpeningDate is not { } od || od <= day ? cash.OpeningBalance : 0)
                          + counted.Where(m => m.Date < day).Sum(m => m.Line.In - m.Line.Out);
            var sheetNumber = days.Count(d => d.Year == day.Year && d <= day);
            var lines = counted.Where(m => m.Date == day)
                .OrderBy(m => m.Line.In > 0 ? 0 : 1).ThenBy(m => m.Line.Number, StringComparer.Ordinal).ThenBy(m => m.Order)
                .Select(m => m.Line).ToList();
            sheets.Add(new CashBookSheetDto(day, sheetNumber, opening, lines));
        }

        return new CashBookDto(cash.Id, cash.BankName, entity.Name, entity.Inn, entity.Kpp, entity.Kind == LegalEntityKind.SoleProprietor,
            entity.AccountantName, entity.DirectorName, from, to, sheets);
    }

    private static void EnsurePeriod(DateOnly from, DateOnly to, int maxDays)
    {
        if (to < from || to.DayNumber - from.DayNumber >= maxDays)
        {
            throw new BusinessRuleException("money.report.period", $"Период — не больше {maxDays} дней, дата «по» не раньше даты «с».");
        }
    }
}
