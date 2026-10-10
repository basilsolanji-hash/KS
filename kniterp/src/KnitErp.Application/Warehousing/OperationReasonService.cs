using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Common;
using KnitErp.Domain.Warehousing;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Warehousing;

public sealed record OperationReasonDto(long Id, StockOperationKind Kind, string Name, bool RequiresComment, bool IsArchived, byte[] RowVersion)
{
    public string KindName => StockOperationKinds.Name(Kind);
}

public sealed record OperationReasonListDto(IReadOnlyList<OperationReasonDto> Reasons, bool CanEdit, bool CanArchive);

/// <summary>Причины складских операций — справочник с правами номенклатуры (допущение D34).</summary>
public sealed class OperationReasonService(IKnitErpDbContext db, IAccessGuard guard, ICurrentUser currentUser, IClock clock)
{
    public async Task<OperationReasonListDto> ListAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.CatalogView, ct);
        var rows = await db.OperationReasons.AsNoTracking()
            .Where(r => r.OrganizationId == ctx.OrganizationId && (includeArchived || !r.IsArchived))
            .OrderBy(r => r.Kind).ThenBy(r => r.Name)
            .Select(r => new OperationReasonDto(r.Id, r.Kind, r.Name, r.RequiresComment, r.IsArchived, r.RowVersion))
            .ToListAsync(ct);
        return new OperationReasonListDto(rows, ctx.Permissions.Has(Permissions.CatalogEdit), ctx.Permissions.Has(Permissions.CatalogArchive));
    }

    public async Task<long> CreateAsync(StockOperationKind kind, string name, bool requiresComment, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.CatalogEdit, ct);
        var reason = OperationReason.Create(ctx.OrganizationId, kind, name, requiresComment);
        await EnsureFreeAsync(ctx, reason.Kind, reason.Name, null, ct);
        db.OperationReasons.Add(reason);
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        Audit(ctx, AuditActions.CatalogCreated, reason.Id, null, reason.Name, StockOperationKinds.Name(kind));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return reason.Id;
    }

    public async Task UpdateAsync(long id, string name, bool requiresComment, byte[] rowVersion, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.CatalogEdit, ct);
        var reason = await FindAsync(ctx, id, ct);
        reason.EnsureVersion(reason.RowVersion, rowVersion);
        var change = reason.Update(name, requiresComment);
        if (change is null)
        {
            return;
        }

        await EnsureFreeAsync(ctx, reason.Kind, reason.Name, id, ct);
        Audit(ctx, AuditActions.CatalogChanged, id, change.Before, change.After, StockOperationKinds.Name(reason.Kind));
        await db.SaveOrConflictAsync(ct);
    }

    public async Task ArchiveAsync(long id, byte[] rowVersion, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.CatalogArchive, ct);
        var reason = await FindAsync(ctx, id, ct);
        reason.EnsureVersion(reason.RowVersion, rowVersion);
        reason.Archive();
        Audit(ctx, AuditActions.CatalogArchived, id, reason.Name, "в архиве", StockOperationKinds.Name(reason.Kind));
        await db.SaveOrConflictAsync(ct);
    }

    /// <summary>Стандартные причины для новой организации. Вызывается при её создании.</summary>
    internal static void SeedDefaults(IKnitErpDbContext db, long organizationId)
    {
        foreach (var (kind, name, requiresComment) in OperationReason.Defaults)
        {
            db.OperationReasons.Add(OperationReason.Create(organizationId, kind, name, requiresComment));
        }
    }

    private async Task<OperationReason> FindAsync(AccessContext ctx, long id, CancellationToken ct) =>
        await db.OperationReasons.SingleOrDefaultAsync(r => r.Id == id && r.OrganizationId == ctx.OrganizationId, ct)
        ?? throw new NotFoundException("Причина операции");

    private async Task EnsureFreeAsync(AccessContext ctx, StockOperationKind kind, string name, long? exceptId, CancellationToken ct)
    {
        if (await db.OperationReasons.AnyAsync(r => r.OrganizationId == ctx.OrganizationId && !r.IsArchived && r.Kind == kind
                                                    && r.Name == name && r.Id != exceptId, ct))
        {
            throw new BusinessRuleException("warehouse.reason.duplicate", $"Причина «{name}» для вида «{StockOperationKinds.Name(kind)}» уже есть.");
        }
    }

    private void Audit(AccessContext ctx, string action, long id, string? before, string? after, string? reason) =>
        db.AuditEntries.Add(AuditEntry.Create(clock.UtcNow, ctx.OrganizationId, ctx.UserId, action, nameof(OperationReason), id.ToString(),
            before, after, reason, currentUser.CorrelationId));
}
