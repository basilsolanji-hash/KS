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
    string ObjectName,
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

        var userIds = entries.Where(e => e.ActorUserId != null).Select(e => e.ActorUserId!.Value)
            .Concat(entries.Where(e => e.EntityType == nameof(UserAccount)).Select(e => long.TryParse(e.EntityId, out var id) ? id : 0))
            .Where(id => id > 0).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var orgName = await db.Organizations.AsNoTracking().Where(o => o.Id == ctx.OrganizationId).Select(o => o.ShortName).SingleAsync(ct);

        return entries.Select(e => new AuditRowDto(
            e.OccurredAtUtc,
            e.ActorUserId,
            e.ActorUserId is { } id && names.TryGetValue(id, out var n) ? n : null,
            e.Action, e.EntityType, e.EntityId, DescribeObject(e, names, orgName), e.Before, e.After, e.Reason)).ToList();
    }

    /// <summary>Объект записи по-человечески: имя пользователя или название организации вместо «UserAccount 2».</summary>
    private static string DescribeObject(AuditEntry e, Dictionary<long, string> userNames, string orgName) => e.EntityType switch
    {
        nameof(UserAccount) when long.TryParse(e.EntityId, out var id) && userNames.TryGetValue(id, out var name) => $"Пользователь «{name}»",
        nameof(UserAccount) => "Пользователь",
        "Organization" => $"Организация «{orgName}»",
        "Department" => $"Подразделение №{e.EntityId}",
        "Position" => $"Должность №{e.EntityId}",
        "Employee" => $"Сотрудник №{e.EntityId}",
        "UnitOfMeasure" => $"Единица №{e.EntityId}",
        "Item" => $"Номенклатура №{e.EntityId}",
        "Site" => $"Площадка №{e.EntityId}",
        "Warehouse" => $"Склад №{e.EntityId}",
        "Counterparty" => $"Контрагент №{e.EntityId}",
        "OperationReason" => $"Причина операции №{e.EntityId}",
        "ItemImport" => "Файл номенклатуры",
        "OpeningBalance" => $"Начальные остатки №{e.EntityId}",
        "StockDocument" => $"Складской документ №{e.EntityId}",
        "Permission" when e.After is { } code => $"Право «{Permissions.Describe(code)}»",
        "Permission" => "Право доступа",
        _ => $"{e.EntityType} {e.EntityId}".Trim(),
    };
}
