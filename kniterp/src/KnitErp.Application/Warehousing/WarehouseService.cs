using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Common;
using KnitErp.Domain.Warehousing;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Warehousing;

public sealed record SiteDto(long Id, string Name, string? Address, int WarehouseCount, bool IsArchived, byte[] RowVersion);

public sealed record WarehouseDto(long Id, string Name, long? SiteId, string? SiteName, bool IsArchived, byte[] RowVersion);

public sealed record WarehousesDto(IReadOnlyList<SiteDto> Sites, IReadOnlyList<WarehouseDto> Warehouses, bool CanEdit, bool CanArchive);

/// <summary>
/// Площадки и склады — справочники организации с теми же правами, что номенклатура (допущение D32).
/// Область склада у кладовщика ограничит остатки и документы, когда они появятся; список складов виден целиком.
/// </summary>
public sealed class WarehouseService(IKnitErpDbContext db, IAccessGuard guard, ICurrentUser currentUser, IClock clock)
{
    public async Task<WarehousesDto> GetAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.CatalogView, ct);
        var sites = await db.Sites.AsNoTracking()
            .Where(s => s.OrganizationId == ctx.OrganizationId && (includeArchived || !s.IsArchived))
            .OrderBy(s => s.Name).ToListAsync(ct);
        var warehouses = await db.Warehouses.AsNoTracking()
            .Where(w => w.OrganizationId == ctx.OrganizationId && (includeArchived || !w.IsArchived))
            .OrderBy(w => w.Name).ToListAsync(ct);
        var siteNames = await db.Sites.AsNoTracking().Where(s => s.OrganizationId == ctx.OrganizationId)
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        return new WarehousesDto(
            sites.Select(s => new SiteDto(s.Id, s.Name, s.Address, warehouses.Count(w => w.SiteId == s.Id && !w.IsArchived), s.IsArchived, s.RowVersion)).ToList(),
            warehouses.Select(w => new WarehouseDto(w.Id, w.Name, w.SiteId, w.SiteId is { } sid ? siteNames.GetValueOrDefault(sid) : null,
                w.IsArchived, w.RowVersion)).ToList(),
            ctx.Permissions.Has(Permissions.CatalogEdit),
            ctx.Permissions.Has(Permissions.CatalogArchive));
    }

    public async Task<long> CreateSiteAsync(string name, string? address, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.CatalogEdit, ct);
        var site = Site.Create(ctx.OrganizationId, name, address);
        await EnsureSiteNameFreeAsync(ctx, site.Name, null, ct);
        db.Sites.Add(site);
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        Audit(ctx, AuditActions.CatalogCreated, nameof(Site), site.Id, null, site.Name, site.Address);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return site.Id;
    }

    public async Task UpdateSiteAsync(long id, string name, string? address, byte[] rowVersion, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.CatalogEdit, ct);
        var site = await FindSiteAsync(ctx, id, ct);
        site.EnsureVersion(site.RowVersion, rowVersion);
        var changes = site.Update(name, address);
        await EnsureSiteNameFreeAsync(ctx, site.Name, id, ct);
        foreach (var c in changes)
        {
            Audit(ctx, AuditActions.CatalogChanged, nameof(Site), id, c.Before, c.After, c.Field);
        }

        await db.SaveOrConflictAsync(ct);
    }

    public async Task ArchiveSiteAsync(long id, byte[] rowVersion, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.CatalogArchive, ct);
        var site = await FindSiteAsync(ctx, id, ct);
        site.EnsureVersion(site.RowVersion, rowVersion);
        site.Archive(await db.Warehouses.CountAsync(w => w.OrganizationId == ctx.OrganizationId && w.SiteId == id && !w.IsArchived, ct));
        Audit(ctx, AuditActions.CatalogArchived, nameof(Site), id, site.Name, "в архиве", null);
        await db.SaveOrConflictAsync(ct);
    }

    public async Task<long> CreateWarehouseAsync(string name, long? siteId, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.CatalogEdit, ct);
        var siteName = await RequireActiveSiteNameAsync(ctx, siteId, ct);
        var warehouse = Warehouse.Create(ctx.OrganizationId, name, siteId);
        await EnsureWarehouseNameFreeAsync(ctx, warehouse.Name, null, ct);
        db.Warehouses.Add(warehouse);
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        Audit(ctx, AuditActions.CatalogCreated, nameof(Warehouse), warehouse.Id, null, warehouse.Name, siteName);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return warehouse.Id;
    }

    public async Task UpdateWarehouseAsync(long id, string name, long? siteId, byte[] rowVersion, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.CatalogEdit, ct);
        var warehouse = await FindWarehouseAsync(ctx, id, ct);
        warehouse.EnsureVersion(warehouse.RowVersion, rowVersion);
        var oldSiteName = await RequireSiteNameAsync(ctx, warehouse.SiteId, ct);
        var newSiteName = await RequireActiveSiteNameAsync(ctx, siteId, ct);
        var changes = warehouse.Update(name, siteId);
        await EnsureWarehouseNameFreeAsync(ctx, warehouse.Name, id, ct);
        foreach (var c in changes)
        {
            var isSite = c.Field == nameof(Warehouse.SiteId);
            Audit(ctx, AuditActions.CatalogChanged, nameof(Warehouse), id,
                isSite ? oldSiteName ?? "без площадки" : c.Before, isSite ? newSiteName ?? "без площадки" : c.After,
                isSite ? "Площадка" : c.Field);
        }

        await db.SaveOrConflictAsync(ct);
    }

    public async Task ArchiveWarehouseAsync(long id, byte[] rowVersion, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.CatalogArchive, ct);
        var warehouse = await FindWarehouseAsync(ctx, id, ct);
        warehouse.EnsureVersion(warehouse.RowVersion, rowVersion);
        warehouse.Archive();
        Audit(ctx, AuditActions.CatalogArchived, nameof(Warehouse), id, warehouse.Name, "в архиве", null);
        await db.SaveOrConflictAsync(ct);
    }

    private async Task<Site> FindSiteAsync(AccessContext ctx, long id, CancellationToken ct) =>
        await db.Sites.SingleOrDefaultAsync(s => s.Id == id && s.OrganizationId == ctx.OrganizationId, ct)
        ?? throw new NotFoundException("Площадка");

    private async Task<Warehouse> FindWarehouseAsync(AccessContext ctx, long id, CancellationToken ct) =>
        await db.Warehouses.SingleOrDefaultAsync(w => w.Id == id && w.OrganizationId == ctx.OrganizationId, ct)
        ?? throw new NotFoundException("Склад");

    private async Task<string?> RequireSiteNameAsync(AccessContext ctx, long? siteId, CancellationToken ct) =>
        siteId is { } id
            ? await db.Sites.AsNoTracking().Where(s => s.Id == id && s.OrganizationId == ctx.OrganizationId).Select(s => s.Name).SingleOrDefaultAsync(ct)
              ?? throw new NotFoundException("Площадка")
            : null;

    private async Task<string?> RequireActiveSiteNameAsync(AccessContext ctx, long? siteId, CancellationToken ct)
    {
        if (siteId is not { } id)
        {
            return null;
        }

        var site = await db.Sites.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.OrganizationId == ctx.OrganizationId, ct)
                   ?? throw new NotFoundException("Площадка");
        return site.IsArchived ? throw new BusinessRuleException("catalog.archived", "Площадка в архиве.") : site.Name;
    }

    private async Task EnsureSiteNameFreeAsync(AccessContext ctx, string name, long? exceptId, CancellationToken ct)
    {
        if (await db.Sites.AnyAsync(s => s.OrganizationId == ctx.OrganizationId && !s.IsArchived && s.Name == name && s.Id != exceptId, ct))
        {
            throw new BusinessRuleException("warehouse.site.duplicate", $"Площадка «{name}» уже есть.");
        }
    }

    private async Task EnsureWarehouseNameFreeAsync(AccessContext ctx, string name, long? exceptId, CancellationToken ct)
    {
        if (await db.Warehouses.AnyAsync(w => w.OrganizationId == ctx.OrganizationId && !w.IsArchived && w.Name == name && w.Id != exceptId, ct))
        {
            throw new BusinessRuleException("warehouse.duplicate", $"Склад «{name}» уже есть.");
        }
    }

    private void Audit(AccessContext ctx, string action, string entityType, long id, string? before, string? after, string? reason) =>
        db.AuditEntries.Add(AuditEntry.Create(clock.UtcNow, ctx.OrganizationId, ctx.UserId, action, entityType, id.ToString(),
            before, after, reason, currentUser.CorrelationId));
}
