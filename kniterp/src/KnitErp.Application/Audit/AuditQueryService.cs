using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Audit;

public sealed record AuditRowDto(
    DateTime OccurredAtUtc,
    long? ActorUserId,
    string? ActorName,
    string Action,
    string EntityType,
    string? EntityId,
    string? Before,
    string? After,
    string? Reason);

/// <summary>Чтение журнала аудита: полный — по праву, «только свои» — для роли «Сотрудник» (ТЗ §4.8 KA3644).</summary>
public sealed class AuditQueryService(IKnitErpDbContext db, IAccessGuard guard)
{
    public async Task<IReadOnlyList<AuditRowDto>> ListAsync(int take = 200, CancellationToken ct = default)
    {
        var ctx = await guard.CurrentAsync(ct);
        var query = db.AuditEntries.AsNoTracking().Where(a => a.OrganizationId == ctx.OrganizationId);

        if (!ctx.Permissions.Has(Permissions.AuditLogView))
        {
            if (!ctx.Permissions.HasOwnOnly(Permissions.AuditLogView))
            {
                await guard.DenyAsync(ctx, Permissions.AuditLogView, ct);
            }

            query = query.Where(a => a.ActorUserId == ctx.UserId);
        }

        var entries = await query
            .OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id)
            .Take(Math.Clamp(take, 1, 1000))
            .ToListAsync(ct);

        var actorIds = entries.Where(e => e.ActorUserId != null).Select(e => e.ActorUserId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return entries.Select(e => new AuditRowDto(
            e.OccurredAtUtc,
            e.ActorUserId,
            e.ActorUserId is { } id && names.TryGetValue(id, out var n) ? n : null,
            e.Action, e.EntityType, e.EntityId, e.Before, e.After, e.Reason)).ToList();
    }
}
