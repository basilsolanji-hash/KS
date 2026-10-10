using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Application.Warehousing;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Common;
using KnitErp.Domain.Finance;
using KnitErp.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Finance;

public sealed record MoneyOperationCommand(
    DateOnly Date, MoneyOperationKind Kind, long AccountId, long? TargetAccountId, decimal Amount, string? Party, string? Basis, string? Comment);

/// <summary>
/// Кассовый ордер для печати: КО-1 (приходный) или КО-2 (расходный) по формам постановления Госкомстата России № 88.
/// Expense — расходный. Organization — полное наименование юрлица с ИНН/КПП. Cancelled — ордер отменён (печать с отметкой).
/// </summary>
public sealed record CashOrderPrintDto(
    bool Expense, string Number, DateOnly Date, string Organization, string? Inn, string? Kpp, bool SoleProprietor, decimal Amount, string AmountInWords,
    string? Party, string Basis, string? Comment, string? DirectorPosition, string? DirectorName, string? AccountantName, string Cashbox, bool Cancelled,
    string Vat = MoneyOperationService.NoVat);

public sealed record MoneyOperationFilter(DateOnly? From = null, DateOnly? To = null, long? AccountId = null, bool IncludeCancelled = true);

public sealed record MoneyOperationDto(
    long Id, string Number, DateOnly Date, MoneyOperationKind Kind, bool Cash, long AccountId, string Account, long? TargetAccountId, string? TargetAccount,
    string LegalEntity, decimal Amount, string? Party, string Basis, string? Comment, MoneyOperationStatus Status, string? CancelReason, byte[] RowVersion)
{
    public string KindName => MoneyOperation.KindName(Kind, Cash);
}

/// <summary>
/// Прочие денежные операции (D84): приходные и расходные кассовые ордера, поступления и списания по расчётному счёту,
/// перемещение денег между своими счетами и кассами одного юрлица. Смотреть — «Цены и суммы»; проводить и отменять —
/// «Цены и суммы» вместе с правом записывать оплаты (продажи или закупки). Касса не уходит в минус: выдача и перемещение
/// из кассы — только в пределах остатка, под блокировкой кассы.
/// </summary>
public sealed class MoneyOperationService(IKnitErpDbContext db, IAccessGuard guard, ICurrentUser currentUser, IClock clock)
{
    public const int MaxRows = 1000;

    public async Task<IReadOnlyList<MoneyOperationDto>> ListAsync(MoneyOperationFilter filter, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.PriceView, ct);
        var q = db.MoneyOperations.AsNoTracking().Where(o => o.OrganizationId == ctx.OrganizationId);
        if (filter.From is { } from)
        {
            q = q.Where(o => o.OperationDate >= from);
        }

        if (filter.To is { } to)
        {
            q = q.Where(o => o.OperationDate <= to);
        }

        if (filter.AccountId is { } account)
        {
            q = q.Where(o => o.AccountId == account || o.TargetAccountId == account);
        }

        if (!filter.IncludeCancelled)
        {
            q = q.Where(o => o.Status == MoneyOperationStatus.Posted);
        }

        var rows = await q.OrderByDescending(o => o.OperationDate).ThenByDescending(o => o.Id).Take(MaxRows).ToListAsync(ct);
        var accounts = await AccountsAsync(ctx, ct);
        return rows.Select(o => Map(o, accounts)).ToList();
    }

    public async Task<MoneyOperationDto> GetAsync(long id, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.PriceView, ct);
        var op = await db.MoneyOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id && o.OrganizationId == ctx.OrganizationId, ct)
                 ?? throw new NotFoundException("Денежная операция");
        return Map(op, await AccountsAsync(ctx, ct));
    }

    /// <summary>Кассовый ордер — только для поступления в кассу и выдачи из кассы.</summary>
    public async Task<CashOrderPrintDto> CashOrderAsync(long id, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.PriceView, ct);
        var op = await db.MoneyOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id && o.OrganizationId == ctx.OrganizationId, ct)
                 ?? throw new NotFoundException("Денежная операция");
        var cash = await db.LegalEntityAccounts.AsNoTracking().SingleAsync(a => a.Id == op.AccountId, ct);
        if (!cash.IsCash || op.Kind == MoneyOperationKind.Transfer)
        {
            throw new BusinessRuleException("money.print.not_cash", "Кассовый ордер печатается для поступления в кассу или выдачи из кассы.");
        }

        return await BuildCashOrderAsync(db, ctx.OrganizationId, op.Kind == MoneyOperationKind.Expense, op.Number, op.OperationDate, op.AccountId,
            op.Amount, op.Party, op.Basis, op.Comment, op.Status == MoneyOperationStatus.Cancelled, NoVat, ct);
    }

    public const string NoVat = "без налога (НДС)";

    /// <summary>
    /// Строка «В том числе» ордера: одна ставка НДС во всех строках заказов оплаты — сумма налога по расчётной ставке
    /// (ставка / (100 + ставка)); все строки без НДС — «без налога (НДС)»; разные ставки или аванс без заказа — пусто,
    /// бухгалтер указывает сам.
    /// </summary>
    public static string VatText(decimal amount, IReadOnlyCollection<decimal?> lineRates)
    {
        if (lineRates.Count == 0)
        {
            return string.Empty;
        }

        if (lineRates.All(r => r is null or 0))
        {
            return NoVat;
        }

        var rates = lineRates.Distinct().ToList();
        if (rates.Count != 1)
        {
            return string.Empty;
        }

        var rate = rates[0]!.Value;
        var vat = Money.Round(amount * rate / (100 + rate));
        return string.Create(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), $"НДС {rate:0.##}% — {vat:N2} руб.");
    }

    /// <summary>Кассовый ордер по кассе своего юрлица: реквизиты юрлица, сумма прописью в валюте организации.</summary>
    internal static async Task<CashOrderPrintDto> BuildCashOrderAsync(IKnitErpDbContext db, long org, bool expense, string number, DateOnly date,
        long accountId, decimal amount, string? party, string basis, string? comment, bool cancelled, string vat, CancellationToken ct)
    {
        var cash = await db.LegalEntityAccounts.AsNoTracking().SingleAsync(a => a.Id == accountId && a.OrganizationId == org, ct);
        var entity = await db.LegalEntities.AsNoTracking().SingleAsync(e => e.Id == cash.LegalEntityId, ct);
        var currency = await db.Organizations.AsNoTracking().Where(o => o.Id == org).Select(o => o.CurrencyCode).SingleAsync(ct);
        return new CashOrderPrintDto(expense, number, date, entity.Name, entity.Inn, entity.Kpp, entity.Kind == LegalEntityKind.SoleProprietor, amount,
            AmountInWords.Format(amount, currency), party, basis, comment, entity.DirectorPosition, entity.DirectorName, entity.AccountantName, cash.BankName,
            cancelled, vat);
    }

    public async Task<long> CreateAsync(MoneyOperationCommand cmd, CancellationToken ct = default)
    {
        var ctx = await DemandEditAsync(ct);
        var source = await ActiveAccountAsync(ctx, cmd.AccountId, ct);
        LegalEntityAccount? target = null;
        if (cmd.TargetAccountId is { } targetId)
        {
            target = await ActiveAccountAsync(ctx, targetId, ct);
            if (target.LegalEntityId != source.LegalEntityId)
            {
                throw new BusinessRuleException("money.transfer.entity",
                    "Перемещение — только между счетами и кассами одного юрлица. Между юрлицами деньги передаются займом или оплатой.");
            }
        }

        await ClosedPeriod.EnsureOpenAsync(db, ctx.OrganizationId, cmd.Date, ct);

        // Все проверки — до выдачи номера: счётчик сохраняется сразу, и отказ после него оставил бы счётчик в контексте вкладки.
        MoneyOperation.Create(ctx.OrganizationId, "—", cmd.Date, cmd.Kind, cmd.AccountId, cmd.TargetAccountId, cmd.Amount, cmd.Party, cmd.Basis,
            cmd.Comment, ctx.UserId, clock.UtcNow);
        await using var tx = await db.BeginTransactionAsync(ct);
        if (cmd.Kind != MoneyOperationKind.Income)
        {
            await MoneyService.EnsureCashAsync(db, ctx.OrganizationId, source.Id, cmd.Amount, ct);
        }

        var number = await DocumentNumbers.NextAsync(db, ctx.OrganizationId, MoneyOperation.PrefixFor(cmd.Kind, source.IsCash), ct);
        var op = MoneyOperation.Create(ctx.OrganizationId, number, cmd.Date, cmd.Kind, cmd.AccountId, cmd.TargetAccountId, cmd.Amount, cmd.Party, cmd.Basis,
            cmd.Comment, ctx.UserId, clock.UtcNow);
        db.MoneyOperations.Add(op);
        await db.SaveChangesAsync(ct);
        Audit(ctx, AuditActions.MoneyOperationCreated, op, null, $"{op.Amount:0.00}",
            $"{op.Number}: {MoneyOperation.KindName(op.Kind, source.IsCash)}, {op.Basis}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return op.Id;
    }

    /// <summary>Отмена с причиной. Отмена поступления в кассу или перемещения в кассу не должна увести её в минус.</summary>
    public async Task CancelAsync(long id, string? reason, byte[] rowVersion, CancellationToken ct = default)
    {
        var ctx = await DemandEditAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var op = await db.MoneyOperations.SingleOrDefaultAsync(o => o.Id == id && o.OrganizationId == ctx.OrganizationId, ct)
                 ?? throw new NotFoundException("Денежная операция");
        op.EnsureVersion(op.RowVersion, rowVersion);
        await ClosedPeriod.EnsureOpenAsync(db, ctx.OrganizationId, op.OperationDate, ct);
        var receiverId = op.Kind switch
        {
            MoneyOperationKind.Income => (long?)op.AccountId,
            MoneyOperationKind.Transfer => op.TargetAccountId,
            _ => null,
        };
        if (receiverId is { } receiver)
        {
            await MoneyService.EnsureCashAsync(db, ctx.OrganizationId, receiver, op.Amount, ct);
        }

        op.Cancel(ctx.UserId, reason, clock.UtcNow);
        Audit(ctx, AuditActions.MoneyOperationCancelled, op, $"{op.Amount:0.00}", "отменена", $"{op.Number}: {op.CancelReason}");
        await db.SaveOrConflictAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<LegalEntityAccount> ActiveAccountAsync(AccessContext ctx, long id, CancellationToken ct)
    {
        var account = await db.LegalEntityAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.OrganizationId == ctx.OrganizationId, ct)
                      ?? throw new NotFoundException("Счёт или касса");
        if (account.IsArchived)
        {
            throw new BusinessRuleException("legal_entity.account_archived", "Счёт в архиве.");
        }

        return account;
    }

    private async Task<Dictionary<long, (string Label, string Entity, bool Cash)>> AccountsAsync(AccessContext ctx, CancellationToken ct) =>
        (await db.LegalEntityAccounts.AsNoTracking().Where(a => a.OrganizationId == ctx.OrganizationId)
            .Join(db.LegalEntities.AsNoTracking(), a => a.LegalEntityId, e => e.Id, (a, e) => new { a.Id, a.Kind, a.BankName, a.Account, e.ShortName })
            .ToListAsync(ct))
        .ToDictionary(a => a.Id, a => (MoneyService.Label(a.Kind, a.BankName, a.Account), a.ShortName, a.Kind == MoneyAccountKind.Cash));

    private static MoneyOperationDto Map(MoneyOperation o, IReadOnlyDictionary<long, (string Label, string Entity, bool Cash)> accounts)
    {
        var source = accounts[o.AccountId];
        return new MoneyOperationDto(o.Id, o.Number, o.OperationDate, o.Kind, source.Cash, o.AccountId, source.Label, o.TargetAccountId,
            o.TargetAccountId is { } t ? accounts[t].Label : null, source.Entity, o.Amount, o.Party, o.Basis, o.Comment, o.Status, o.CancelReason,
            o.RowVersion);
    }

    private async Task<AccessContext> DemandEditAsync(CancellationToken ct)
    {
        var ctx = await guard.DemandAsync(Permissions.PriceView, ct);
        if (!ctx.Permissions.Has(Permissions.SalesEdit) && !ctx.Permissions.Has(Permissions.PurchaseEdit))
        {
            await guard.DenyAsync(ctx, Permissions.SalesEdit, ct);
        }

        return ctx;
    }

    private void Audit(AccessContext ctx, string action, MoneyOperation op, string? before, string? after, string? reason) =>
        db.AuditEntries.Add(AuditEntry.Create(clock.UtcNow, ctx.OrganizationId, ctx.UserId, action, nameof(MoneyOperation), op.Id.ToString(),
            before, after, reason, currentUser.CorrelationId));
}
