using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Common;
using KnitErp.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Finance;

public sealed record CashFlowItemDto(long Id, CashFlowDirection Direction, string Name, string? SystemCode, bool IsArchived, byte[] RowVersion)
{
    public bool IsSystem => SystemCode is not null;
}

/// <summary>
/// Справочник статей движения денег (D86). Смотреть — «Цены и суммы»; добавлять, переименовывать и архивировать — право изменения
/// реквизитов организации (как юрлица и счета). Системные статьи не меняются.
/// </summary>
public sealed class CashFlowItemService(IKnitErpDbContext db, IAccessGuard guard, ICurrentUser currentUser, IClock clock)
{
    public async Task<IReadOnlyList<CashFlowItemDto>> ListAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.PriceView, ct);
        return await db.CashFlowItems.AsNoTracking()
            .Where(i => i.OrganizationId == ctx.OrganizationId && (includeArchived || !i.IsArchived))
            .OrderBy(i => i.Direction).ThenBy(i => i.SystemCode == null).ThenBy(i => i.Name)
            .Select(i => new CashFlowItemDto(i.Id, i.Direction, i.Name, i.SystemCode, i.IsArchived, i.RowVersion))
            .ToListAsync(ct);
    }

    public async Task<long> CreateAsync(CashFlowDirection direction, string? name, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.OrganizationEdit, ct);
        if (!Enum.IsDefined(direction))
        {
            throw new BusinessRuleException("cash_flow_item.direction", "Укажите направление: поступление или выплата.");
        }

        var item = CashFlowItem.Create(ctx.OrganizationId, direction, name);
        await EnsureFreeAsync(ctx, direction, item.Name, null, ct);
        db.CashFlowItems.Add(item);
        await db.SaveChangesAsync(ct);
        Audit(ctx, AuditActions.CatalogCreated, item.Id, null, item.Name, CashFlowItem.DirectionName(direction));
        await db.SaveChangesAsync(ct);
        return item.Id;
    }

    public async Task RenameAsync(long id, string? name, byte[] rowVersion, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.OrganizationEdit, ct);
        var item = await FindAsync(ctx, id, ct);
        item.EnsureVersion(item.RowVersion, rowVersion);
        var before = item.Name;
        item.Rename(name);
        await EnsureFreeAsync(ctx, item.Direction, item.Name, id, ct);
        Audit(ctx, AuditActions.CatalogChanged, id, before, item.Name, "Статья ДДС");
        await db.SaveOrConflictAsync(ct);
    }

    public async Task SetArchivedAsync(long id, bool archived, byte[] rowVersion, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.OrganizationEdit, ct);
        var item = await FindAsync(ctx, id, ct);
        item.EnsureVersion(item.RowVersion, rowVersion);
        if (!archived)
        {
            await EnsureFreeAsync(ctx, item.Direction, item.Name, id, ct);
        }

        item.SetArchived(archived);
        Audit(ctx, archived ? AuditActions.CatalogArchived : AuditActions.CatalogChanged, id, item.Name, archived ? "в архиве" : "действует",
            "Статья ДДС");
        await db.SaveOrConflictAsync(ct);
    }

    /// <summary>Стандартные статьи для новой организации. Вызывается при её создании.</summary>
    internal static void SeedDefaults(IKnitErpDbContext db, long organizationId)
    {
        foreach (var (direction, name, code) in CashFlowItem.Defaults)
        {
            db.CashFlowItems.Add(CashFlowItem.Create(organizationId, direction, name, code));
        }
    }

    /// <summary>Системная статья организации по коду (создаётся вместе с организацией).</summary>
    internal static async Task<CashFlowItem> SystemAsync(IKnitErpDbContext db, long org, string code, CancellationToken ct) =>
        await db.CashFlowItems.AsNoTracking().SingleOrDefaultAsync(i => i.OrganizationId == org && i.SystemCode == code, ct)
        ?? throw new BusinessRuleException("cash_flow_item.missing", "Нет стандартной статьи движения денег — обратитесь к администратору.");

    private async Task<CashFlowItem> FindAsync(AccessContext ctx, long id, CancellationToken ct) =>
        await db.CashFlowItems.SingleOrDefaultAsync(i => i.Id == id && i.OrganizationId == ctx.OrganizationId, ct)
        ?? throw new NotFoundException("Статья движения денег");

    private async Task EnsureFreeAsync(AccessContext ctx, CashFlowDirection direction, string name, long? exceptId, CancellationToken ct)
    {
        if (await db.CashFlowItems.AnyAsync(i => i.OrganizationId == ctx.OrganizationId && i.Direction == direction && i.Name == name
                                                 && i.Id != exceptId, ct))
        {
            throw new BusinessRuleException("cash_flow_item.duplicate", $"Статья «{name}» уже есть (возможно, в архиве).");
        }
    }

    private void Audit(AccessContext ctx, string action, long id, string? before, string? after, string? reason) =>
        db.AuditEntries.Add(AuditEntry.Create(clock.UtcNow, ctx.OrganizationId, ctx.UserId, action, nameof(CashFlowItem), id.ToString(),
            before, after, reason, currentUser.CorrelationId));
}
