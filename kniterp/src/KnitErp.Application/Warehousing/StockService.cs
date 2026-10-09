using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Warehousing;

public sealed record StockBalanceDto(
    long WarehouseId, string WarehouseName, long ItemId, string Code, string Name, ItemType Type, string UnitSymbol, decimal Quantity)
{
    public string TypeName => ItemTypes.Name(Type);
}

public sealed record StockFilter(long? WarehouseId = null, ItemType? Type = null, string? Search = null);

/// <summary>Остатки = сумма движений регистра. Кладовщик со складом видит только свой склад.</summary>
public sealed class StockService(IKnitErpDbContext db, IAccessGuard guard)
{
    public async Task<IReadOnlyList<StockBalanceDto>> BalancesAsync(StockFilter filter, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.WarehouseReportView, ct);
        var visible = WarehouseScope.Visible(ctx, Permissions.WarehouseReportView);
        var movements = db.StockMovements.AsNoTracking().Where(m => m.OrganizationId == ctx.OrganizationId);
        if (visible is not null)
        {
            movements = movements.Where(m => visible.Contains(m.WarehouseId));
        }

        if (filter.WarehouseId is { } wh)
        {
            movements = movements.Where(m => m.WarehouseId == wh);
        }

        var sums = movements.GroupBy(m => new { m.WarehouseId, m.ItemId })
            .Select(g => new { g.Key.WarehouseId, g.Key.ItemId, Quantity = g.Sum(m => m.Quantity) })
            .Where(x => x.Quantity != 0);

        var rows = from s in sums
                   join i in db.Items.AsNoTracking() on s.ItemId equals i.Id
                   join u in db.Units.AsNoTracking() on i.UnitId equals u.Id
                   join w in db.Warehouses.AsNoTracking() on s.WarehouseId equals w.Id
                   select new { s, i, u.Symbol, WarehouseName = w.Name };
        if (filter.Type is { } type)
        {
            rows = rows.Where(r => r.i.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var text = filter.Search.Trim();
            rows = rows.Where(r => r.i.Code.Contains(text) || r.i.Name.Contains(text));
        }

        return await rows.OrderBy(r => r.WarehouseName).ThenBy(r => r.i.Type).ThenBy(r => r.i.Name).Take(5000)
            .Select(r => new StockBalanceDto(r.s.WarehouseId, r.WarehouseName, r.i.Id, r.i.Code, r.i.Name, r.i.Type, r.Symbol, r.s.Quantity))
            .ToListAsync(ct);
    }

    /// <summary>Склады, по которым пользователь видит остатки, — для фильтра.</summary>
    public async Task<IReadOnlyList<LookupWarehouseDto>> ListWarehousesAsync(CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.WarehouseReportView, ct);
        var visible = WarehouseScope.Visible(ctx, Permissions.WarehouseReportView);
        return await db.Warehouses.AsNoTracking()
            .Where(w => w.OrganizationId == ctx.OrganizationId && !w.IsArchived && (visible == null || visible.Contains(w.Id)))
            .OrderBy(w => w.Name).Select(w => new LookupWarehouseDto(w.Id, w.Name)).ToListAsync(ct);
    }
}
