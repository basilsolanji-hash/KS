using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;

namespace KnitErp.Application.Security;

public sealed record IntegrityProblem(string Journal, long RecordId, string Description);

/// <summary>
/// Итог проверки одного журнала. Legacy — записи до включения подписи (их проверить нельзя).
/// Fingerprint — отпечаток последней подписи: если записать его на бумагу, позже видно, что хвост журнала не срезан.
/// </summary>
public sealed record JournalCheck(string Journal, int Checked, int Legacy, string? Fingerprint, IReadOnlyList<IntegrityProblem> Problems)
{
    public bool Ok => Problems.Count == 0;
}

public interface IIntegrityVerifier
{
    Task<IReadOnlyList<JournalCheck>> VerifyAsync(long organizationId, CancellationToken ct = default);
}

/// <summary>
/// «Проверка целостности»: пересчёт подписей журнала аудита и движений склада своей организации.
/// Доступна тем, кто видит весь журнал аудита (Владелец, Администратор, Аудитор). Сама проверка пишется в журнал.
/// </summary>
public sealed class IntegrityService(IKnitErpDbContext db, IAccessGuard guard, IIntegrityVerifier verifier, ICurrentUser currentUser, IClock clock)
{
    public async Task<IReadOnlyList<JournalCheck>> VerifyAsync(CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.AuditLogView, ct);
        var result = await verifier.VerifyAsync(ctx.OrganizationId, ct);
        var problems = result.Sum(r => r.Problems.Count);
        db.AuditEntries.Add(AuditEntry.Create(clock.UtcNow, ctx.OrganizationId, ctx.UserId, AuditActions.IntegrityChecked, "Integrity",
            null, null, problems == 0 ? "нарушений нет" : $"нарушений: {problems}",
            string.Join("; ", result.Select(r => $"{r.Journal}: {r.Checked}")), currentUser.CorrelationId));
        await db.SaveChangesAsync(ct);
        return result;
    }
}
