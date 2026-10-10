using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Common;
using KnitErp.Domain.Organizations;
using KnitErp.Domain.Purchasing;
using KnitErp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Finance;

/// <summary>Остаток денег на счёте или в кассе: начальный + поступления − выплаты (с даты начального остатка).</summary>
public sealed record MoneyAccountBalanceDto(
    long? AccountId, long? LegalEntityId, string LegalEntity, MoneyAccountKind? Kind, string Name, decimal Opening, decimal In, decimal Out,
    bool IsArchived)
{
    public decimal Balance => Opening + In - Out;
}

/// <summary>Счёт или касса для выбора в оплате: «ООО «Фабрика» · Сбербанк …0001» или «ИП Петров · Касса цеха».</summary>
public sealed record MoneyAccountOptionDto(long Id, long LegalEntityId, string Label, MoneyAccountKind Kind, bool IsDefault);

/// <summary>
/// Деньги на счетах и в кассах всех своих юрлиц (D80). Поступления — проведённые оплаты покупателей, выплаты — проведённые
/// оплаты поставщикам; считаются с даты начального остатка счёта. Оплаты без счёта (заведены до D80 и не разнесены)
/// показываются отдельной строкой «Без счёта», чтобы итог сходился с расчётами. Право — «Цены и суммы».
/// </summary>
public sealed class MoneyService(IKnitErpDbContext db, IAccessGuard guard)
{
    public async Task<IReadOnlyList<MoneyAccountBalanceDto>> BalancesAsync(DateOnly? asOf = null, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.PriceView, ct);
        return await BalancesAsync(db, ctx.OrganizationId, asOf, includeArchived: false, ct);
    }

    internal static async Task<IReadOnlyList<MoneyAccountBalanceDto>> BalancesAsync(IKnitErpDbContext db, long org, DateOnly? asOf, bool includeArchived,
        CancellationToken ct)
    {
        var accounts = await db.LegalEntityAccounts.AsNoTracking().Where(a => a.OrganizationId == org)
            .Join(db.LegalEntities.AsNoTracking(), a => a.LegalEntityId, e => e.Id, (a, e) => new
            {
                a.Id, a.LegalEntityId, Entity = e.ShortName, EntityDefault = e.IsDefault, a.Kind, a.BankName, a.Account, a.OpeningBalance, a.OpeningDate,
                a.IsArchived, a.IsDefault,
            })
            .ToListAsync(ct);
        var opening = accounts.ToDictionary(a => a.Id, a => a.OpeningDate);

        // Суммы по счёту и дню в SQL; отсечение по дате начального остатка — здесь (у каждого счёта своя дата).
        var incoming = await db.CustomerPayments.AsNoTracking()
            .Where(p => p.OrganizationId == org && p.Status == CustomerPaymentStatus.Posted && (asOf == null || p.PaymentDate <= asOf))
            .GroupBy(p => new { p.MoneyAccountId, p.PaymentDate }).Select(g => new { g.Key.MoneyAccountId, g.Key.PaymentDate, Sum = g.Sum(p => p.Amount) })
            .ToListAsync(ct);
        var outgoing = await db.SupplierPayments.AsNoTracking()
            .Where(p => p.OrganizationId == org && p.Status == SupplierPaymentStatus.Posted && (asOf == null || p.PaymentDate <= asOf))
            .GroupBy(p => new { p.MoneyAccountId, p.PaymentDate }).Select(g => new { g.Key.MoneyAccountId, g.Key.PaymentDate, Sum = g.Sum(p => p.Amount) })
            .ToListAsync(ct);
        // Прочие поступления, выдачи и перемещения (D84): перемещение — расход со счёта-источника и приход на счёт-получатель.
        var operations = await db.MoneyOperations.AsNoTracking()
            .Where(o => o.OrganizationId == org && o.Status == KnitErp.Domain.Finance.MoneyOperationStatus.Posted && (asOf == null || o.OperationDate <= asOf))
            .GroupBy(o => new { o.Kind, o.AccountId, o.TargetAccountId, o.OperationDate })
            .Select(g => new { g.Key.Kind, g.Key.AccountId, g.Key.TargetAccountId, g.Key.OperationDate, Sum = g.Sum(o => o.Amount) })
            .ToListAsync(ct);
        IEnumerable<(long? Account, DateOnly Day, decimal Sum)> ins = incoming.Select(x => (x.MoneyAccountId, x.PaymentDate, x.Sum))
            .Concat(operations.Where(o => o.Kind == KnitErp.Domain.Finance.MoneyOperationKind.Income).Select(o => ((long?)o.AccountId, o.OperationDate, o.Sum)))
            .Concat(operations.Where(o => o.Kind == KnitErp.Domain.Finance.MoneyOperationKind.Transfer).Select(o => (o.TargetAccountId, o.OperationDate, o.Sum)));
        IEnumerable<(long? Account, DateOnly Day, decimal Sum)> outs = outgoing.Select(x => (x.MoneyAccountId, x.PaymentDate, x.Sum))
            .Concat(operations.Where(o => o.Kind != KnitErp.Domain.Finance.MoneyOperationKind.Income).Select(o => ((long?)o.AccountId, o.OperationDate, o.Sum)));
        bool Counts(long? accountId, DateOnly day) =>
            accountId is not { } id || !opening.TryGetValue(id, out var from) || from is null || day >= from;
        var inSum = ins.Where(x => Counts(x.Account, x.Day)).GroupBy(x => x.Account ?? 0).ToDictionary(g => g.Key, g => g.Sum(x => x.Sum));
        var outSum = outs.Where(x => Counts(x.Account, x.Day)).GroupBy(x => x.Account ?? 0).ToDictionary(g => g.Key, g => g.Sum(x => x.Sum));

        var rows = accounts
            .Where(a => includeArchived || !a.IsArchived || inSum.ContainsKey(a.Id) || outSum.ContainsKey(a.Id) || a.OpeningBalance != 0)
            .OrderByDescending(a => a.EntityDefault).ThenBy(a => a.Entity).ThenBy(a => a.Kind).ThenByDescending(a => a.IsDefault).ThenBy(a => a.Id)
            .Select(a => new MoneyAccountBalanceDto(a.Id, a.LegalEntityId, a.Entity, a.Kind, Label(a.Kind, a.BankName, a.Account),
                a.OpeningDate is { } d && asOf is { } on && d > on ? 0 : a.OpeningBalance,
                inSum.GetValueOrDefault(a.Id), outSum.GetValueOrDefault(a.Id), a.IsArchived))
            .ToList();
        if (inSum.ContainsKey(0) || outSum.ContainsKey(0))
        {
            rows.Add(new MoneyAccountBalanceDto(null, null, string.Empty, null, "Без счёта", 0, inSum.GetValueOrDefault(0), outSum.GetValueOrDefault(0), false));
        }

        return rows;
    }

    /// <summary>Действующие счета и кассы всех юрлиц — для выбора в оплате; основной счёт основного юрлица первым.</summary>
    public async Task<IReadOnlyList<MoneyAccountOptionDto>> OptionsAsync(CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.PriceView, ct);
        return await OptionsAsync(db, ctx.OrganizationId, ct);
    }

    internal static async Task<IReadOnlyList<MoneyAccountOptionDto>> OptionsAsync(IKnitErpDbContext db, long org, CancellationToken ct) =>
        (await db.LegalEntityAccounts.AsNoTracking().Where(a => a.OrganizationId == org && !a.IsArchived)
            .Join(db.LegalEntities.AsNoTracking().Where(e => !e.IsArchived), a => a.LegalEntityId, e => e.Id,
                (a, e) => new { a.Id, a.LegalEntityId, e.ShortName, EntityDefault = e.IsDefault, a.Kind, a.BankName, a.Account, a.IsDefault })
            .ToListAsync(ct))
        .OrderByDescending(a => a.EntityDefault && a.IsDefault).ThenByDescending(a => a.EntityDefault).ThenBy(a => a.ShortName).ThenBy(a => a.Kind)
        .ThenByDescending(a => a.IsDefault).ThenBy(a => a.Id)
        .Select(a => new MoneyAccountOptionDto(a.Id, a.LegalEntityId, $"{a.ShortName} · {Label(a.Kind, a.BankName, a.Account)}", a.Kind, a.IsDefault))
        .ToList();

    /// <summary>
    /// Счёт оплаты: указанный — проверяется (своя организация, действующий); не указан — счёт заказа или основной счёт его юрлица,
    /// без заказа — основной счёт основного юрлица; счетов нет — null.
    /// </summary>
    internal static async Task<long?> ResolveAsync(IKnitErpDbContext db, long org, long? accountId, long? legalEntityId, long? orderAccountId,
        CancellationToken ct)
    {
        if (accountId is { } id)
        {
            var account = await db.LegalEntityAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.OrganizationId == org, ct)
                          ?? throw new NotFoundException("Счёт или касса");
            if (account.IsArchived)
            {
                throw new BusinessRuleException("legal_entity.account_archived", "Счёт в архиве.");
            }

            if (legalEntityId is { } entityOfOrder && account.LegalEntityId != entityOfOrder)
            {
                throw new BusinessRuleException("money.account_entity", "Счёт или касса другого юрлица, не того, что в заказе.");
            }

            return id;
        }

        if (orderAccountId is { } fromOrder)
        {
            return fromOrder;
        }

        var entity = legalEntityId ?? await db.LegalEntities.AsNoTracking().Where(e => e.OrganizationId == org && e.IsDefault)
            .Select(e => (long?)e.Id).FirstOrDefaultAsync(ct);
        return entity is null ? null : await db.LegalEntityAccounts.AsNoTracking()
            .Where(a => a.OrganizationId == org && a.LegalEntityId == entity && a.IsDefault && !a.IsArchived)
            .Select(a => (long?)a.Id).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Касса не уходит в минус (D84): выдача из кассы — только в пределах остатка. Касса блокируется до конца транзакции,
    /// параллельная выдача ждёт. Для расчётного счёта проверки нет (возможен овердрафт). Вызывать внутри транзакции.
    /// </summary>
    internal static async Task EnsureCashAsync(IKnitErpDbContext db, long org, long accountId, decimal amount, CancellationToken ct)
    {
        var cash = await db.LegalEntityAccounts.AsNoTracking().SingleAsync(a => a.Id == accountId && a.OrganizationId == org, ct);
        if (!cash.IsCash)
        {
            return;
        }

        await db.LockAsync($"kniterp.money.account.{cash.Id}", ct);
        var balance = (await BalancesAsync(db, org, null, includeArchived: true, ct)).FirstOrDefault(b => b.AccountId == cash.Id)?.Balance ?? 0m;
        if (balance < amount)
        {
            throw new BusinessRuleException("money.cash.insufficient",
                $"В кассе «{cash.BankName}» {balance:0.00}, а нужно {amount:0.00}. Касса не может уйти в минус — сначала оформите поступление.");
        }
    }

    public static string Label(MoneyAccountKind kind, string name, string account) =>
        kind == MoneyAccountKind.Cash ? $"Касса «{name}»" : $"{name}, р/с …{account[^Math.Min(4, account.Length)..]}";
}
