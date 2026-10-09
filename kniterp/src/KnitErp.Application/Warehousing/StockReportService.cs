using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Warehousing;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Warehousing;

public sealed record MovementFilter(long? WarehouseId, DateOnly? From, DateOnly? To, string? Search);

public sealed record MovementRowDto(
    DateOnly Date, string Document, string? Link, string WarehouseName, string Code, string Name, string UnitSymbol,
    decimal Incoming, decimal Outgoing);

public sealed record TurnoverFilter(long? WarehouseId, DateOnly From, DateOnly To, ItemType? Type, string? Search);

public sealed record TurnoverRowDto(
    string WarehouseName, string Code, string Name, string UnitSymbol, decimal Opening, decimal Incoming, decimal Outgoing)
{
    public decimal Closing => Opening + Incoming - Outgoing;
}

public sealed record MovementReport(IReadOnlyList<MovementRowDto> Rows, bool Truncated);

public sealed record InventoryVarianceFilter(long? WarehouseId, DateOnly From, DateOnly To, string? Search);

/// <summary>Строка отклонения: факт − учёт; плюс — излишек, минус — недостача.</summary>
public sealed record InventoryVarianceRowDto(
    DateOnly Date, long InventoryId, string Number, string WarehouseName, string Code, string Name, string UnitSymbol,
    decimal Book, decimal Counted, string? Comment)
{
    public decimal Difference => Counted - Book;
}

public sealed record InventoryVarianceReport(IReadOnlyList<InventoryVarianceRowDto> Rows, int Documents)
{
    public int SurplusLines => Rows.Count(r => r.Difference > 0);
    public int ShortageLines => Rows.Count(r => r.Difference < 0);
}

/// <summary>
/// Отчёты по регистру движений: «Движения» — каждая строка регистра с документом-источником,
/// «Обороты» — остаток на начало, приход, расход и остаток на конец периода. Область склада — как у «Остатков».
/// Сторно уменьшает обороты своего вида, а не попадает в противоположный («красное сторно», допущение D45).
/// </summary>
public sealed class StockReportService(IKnitErpDbContext db, IAccessGuard guard)
{
    public const int MaxMovementRows = 2000;

    public async Task<MovementReport> MovementsAsync(MovementFilter filter, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.WarehouseReportView, ct);
        var q = Scoped(ctx, filter.WarehouseId);
        if (filter.From is { } from)
        {
            q = q.Where(m => m.OccurredOn >= from);
        }

        if (filter.To is { } to)
        {
            q = q.Where(m => m.OccurredOn <= to);
        }

        var rows = from m in q
                   join i in db.Items.AsNoTracking() on m.ItemId equals i.Id
                   join u in db.Units.AsNoTracking() on i.UnitId equals u.Id
                   join w in db.Warehouses.AsNoTracking() on m.WarehouseId equals w.Id
                   select new { m, i.Code, i.Name, u.Symbol, Warehouse = w.Name };
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var text = filter.Search.Trim();
            rows = rows.Where(r => r.Code.Contains(text) || r.Name.Contains(text));
        }

        var list = await rows.OrderByDescending(r => r.m.OccurredOn).ThenByDescending(r => r.m.Id).Take(MaxMovementRows + 1).ToListAsync(ct);
        var names = await DocumentNamesAsync(list.Select(r => (r.m.Source, r.m.SourceId)).Distinct().ToList(), ct);
        var result = list.Take(MaxMovementRows).Select(r =>
        {
            var (doc, link) = names[(r.m.Source, r.m.SourceId)];
            if (r.m.Source == StockSource.StockDocumentReversal)
            {
                doc = $"Сторно {doc}";
            }

            return new MovementRowDto(r.m.OccurredOn, doc, link, r.Warehouse, r.Code, r.Name, r.Symbol,
                r.m.Quantity > 0 ? r.m.Quantity : 0, r.m.Quantity < 0 ? -r.m.Quantity : 0);
        }).ToList();
        return new MovementReport(result, list.Count > MaxMovementRows);
    }

    public async Task<IReadOnlyList<TurnoverRowDto>> TurnoverAsync(TurnoverFilter filter, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.WarehouseReportView, ct);
        if (filter.To < filter.From)
        {
            throw new Domain.Common.BusinessRuleException("report.period", "Дата окончания раньше даты начала.");
        }

        var from = filter.From;
        var to = filter.To;
        var reversal = StockSource.StockDocumentReversal;
        var sums = Scoped(ctx, filter.WarehouseId).Where(m => m.OccurredOn <= to)
            .GroupBy(m => new { m.WarehouseId, m.ItemId })
            .Select(g => new
            {
                g.Key.WarehouseId,
                g.Key.ItemId,
                Opening = g.Where(m => m.OccurredOn < from).Sum(m => (decimal?)m.Quantity) ?? 0,
                Incoming = g.Where(m => m.OccurredOn >= from && ((m.Quantity > 0 && m.Source != reversal) || (m.Quantity < 0 && m.Source == reversal)))
                    .Sum(m => (decimal?)m.Quantity) ?? 0,
                Outgoing = g.Where(m => m.OccurredOn >= from && ((m.Quantity < 0 && m.Source != reversal) || (m.Quantity > 0 && m.Source == reversal)))
                    .Sum(m => (decimal?)-m.Quantity) ?? 0,
            });

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

        var list = await rows.OrderBy(r => r.WarehouseName).ThenBy(r => r.i.Type).ThenBy(r => r.i.Name).Take(5000)
            .Select(r => new TurnoverRowDto(r.WarehouseName, r.i.Code, r.i.Name, r.Symbol, r.s.Opening, r.s.Incoming, r.s.Outgoing))
            .ToListAsync(ct);
        return list.Where(r => r.Opening != 0 || r.Incoming != 0 || r.Outgoing != 0).ToList();
    }

    /// <summary>
    /// Отклонения инвентаризации: строки проведённых инвентаризаций за период, где факт не совпал с учётом.
    /// Учёт — на момент проведения, поэтому отчёт показывает то, что ушло в излишки и недостачи.
    /// </summary>
    public async Task<InventoryVarianceReport> InventoryVariancesAsync(InventoryVarianceFilter filter, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.WarehouseReportView, ct);
        if (filter.To < filter.From)
        {
            throw new Domain.Common.BusinessRuleException("report.period", "Дата окончания раньше даты начала.");
        }

        var visible = WarehouseScope.Visible(ctx, Permissions.WarehouseReportView);
        var docs = db.InventoryCounts.AsNoTracking().Where(d => d.OrganizationId == ctx.OrganizationId && d.Status == InventoryStatus.Posted
                                                                && d.CountDate >= filter.From && d.CountDate <= filter.To);
        if (visible is not null)
        {
            docs = docs.Where(d => visible.Contains(d.WarehouseId));
        }

        if (filter.WarehouseId is { } wh)
        {
            docs = docs.Where(d => d.WarehouseId == wh);
        }

        var documents = await docs.CountAsync(ct);
        var rows = from d in docs
                   from l in d.Lines
                   where l.CountedQuantity != null && l.CountedQuantity != l.BookQuantity
                   join i in db.Items.AsNoTracking() on l.ItemId equals i.Id
                   join u in db.Units.AsNoTracking() on i.UnitId equals u.Id
                   join w in db.Warehouses.AsNoTracking() on d.WarehouseId equals w.Id
                   select new { d, l, i.Code, i.Name, u.Symbol, Warehouse = w.Name };
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var text = filter.Search.Trim();
            rows = rows.Where(r => r.Code.Contains(text) || r.Name.Contains(text));
        }

        var list = await rows.OrderBy(r => r.d.CountDate).ThenBy(r => r.d.Id).ThenBy(r => r.Name).Take(5000)
            .Select(r => new InventoryVarianceRowDto(r.d.CountDate, r.d.Id, r.d.Number, r.Warehouse, r.Code, r.Name, r.Symbol,
                r.l.BookQuantity, r.l.CountedQuantity!.Value, r.d.Comment))
            .ToListAsync(ct);
        return new InventoryVarianceReport(list, documents);
    }

    private IQueryable<StockMovement> Scoped(AccessContext ctx, long? warehouseId)
    {
        var visible = WarehouseScope.Visible(ctx, Permissions.WarehouseReportView);
        var q = db.StockMovements.AsNoTracking().Where(m => m.OrganizationId == ctx.OrganizationId);
        if (visible is not null)
        {
            q = q.Where(m => visible.Contains(m.WarehouseId));
        }

        if (warehouseId is { } wh)
        {
            q = q.Where(m => m.WarehouseId == wh);
        }

        return q;
    }

    /// <summary>Номер и ссылка документа-источника для каждой пары (вид источника, id).</summary>
    private async Task<Dictionary<(StockSource, long), (string, string?)>> DocumentNamesAsync(
        IReadOnlyList<(StockSource Source, long Id)> sources, CancellationToken ct)
    {
        var result = new Dictionary<(StockSource, long), (string, string?)>();
        var opening = sources.Where(s => s.Source == StockSource.OpeningBalance).Select(s => s.Id).ToList();
        foreach (var d in await db.OpeningBalances.AsNoTracking().Where(d => opening.Contains(d.Id)).Select(d => new { d.Id, d.Number }).ToListAsync(ct))
        {
            result[(StockSource.OpeningBalance, d.Id)] = ($"Начальные остатки {d.Number}", $"opening-balances/{d.Id}");
        }

        var docs = sources.Where(s => s.Source is StockSource.StockDocument or StockSource.StockDocumentReversal).Select(s => s.Id).Distinct().ToList();
        foreach (var d in await db.StockDocuments.AsNoTracking().Where(d => docs.Contains(d.Id)).Select(d => new { d.Id, d.Number, d.Kind }).ToListAsync(ct))
        {
            var text = $"{StockDocument.KindName(d.Kind)} {d.Number}";
            result[(StockSource.StockDocument, d.Id)] = (text, $"stock-documents/{d.Id}");
            result[(StockSource.StockDocumentReversal, d.Id)] = (text, $"stock-documents/{d.Id}");
        }

        var inventories = sources.Where(s => s.Source == StockSource.Inventory).Select(s => s.Id).ToList();
        foreach (var d in await db.InventoryCounts.AsNoTracking().Where(d => inventories.Contains(d.Id)).Select(d => new { d.Id, d.Number }).ToListAsync(ct))
        {
            result[(StockSource.Inventory, d.Id)] = ($"Инвентаризация {d.Number}", $"inventory/{d.Id}");
        }

        foreach (var s in sources.Where(s => !result.ContainsKey((s.Source, s.Id))))
        {
            result[(s.Source, s.Id)] = ($"Документ №{s.Id}", null);
        }

        return result;
    }
}
