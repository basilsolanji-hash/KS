using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Common;
using KnitErp.Domain.Warehousing;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Warehousing;

public sealed record ReconciliationProblem(string Document, string? Link, string Description);

public sealed record ReconciliationResult(
    int DocumentsChecked, int MovementsChecked, int PositionsChecked, IReadOnlyList<ReconciliationProblem> Problems)
{
    public bool Ok => Problems.Count == 0;
}

/// <summary>
/// Сверка регистра движений с документами: каждое движение должно быть ровно таким, каким его требует свой документ
/// (утверждённые начальные остатки, проведённые и сторнированные документы, проведённые инвентаризации), у черновиков
/// и отменённых документов движений нет, движений без документа нет, остаток нигде не отрицательный (D40).
/// Расхождение означает сбой или правку данных в обход программы — вместе с «Проверкой целостности» это контроль
/// перед закрытием периода. Область складов — как у «Остатков».
/// </summary>
public sealed class ReconciliationService(IKnitErpDbContext db, IAccessGuard guard)
{
    public const int MaxProblems = 200;

    public async Task<ReconciliationResult> RunAsync(CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.WarehouseReportView, ct);
        var org = ctx.OrganizationId;
        var visible = WarehouseScope.Visible(ctx, Permissions.WarehouseReportView);
        bool InScope(long warehouseId) => visible is null || visible.Contains(warehouseId);

        var movements = await db.StockMovements.AsNoTracking().Where(m => m.OrganizationId == org)
            .Select(m => new { m.Source, m.SourceId, m.WarehouseId, m.ItemId, m.Quantity }).ToListAsync(ct);
        movements = movements.Where(m => InScope(m.WarehouseId)).ToList();
        var actual = movements.GroupBy(m => (m.Source, m.SourceId, m.WarehouseId, m.ItemId))
            .ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));

        var expected = new Dictionary<(StockSource, long, long, long), decimal>();
        var documents = new Dictionary<(StockSource, long), (string Name, string Link)>();
        void Expect(StockSource source, long id, long warehouse, long item, decimal qty)
        {
            if (qty == 0 || !InScope(warehouse))
            {
                return;
            }

            var key = (source, id, warehouse, item);
            expected[key] = expected.GetValueOrDefault(key) + qty;
        }

        foreach (var d in await db.OpeningBalances.AsNoTracking().Include(d => d.Lines).Where(d => d.OrganizationId == org).ToListAsync(ct))
        {
            documents[(StockSource.OpeningBalance, d.Id)] = ($"Начальные остатки {d.Number}", $"opening-balances/{d.Id}");
            if (d.Status == OpeningBalanceStatus.Approved)
            {
                foreach (var l in d.Lines)
                {
                    Expect(StockSource.OpeningBalance, d.Id, d.WarehouseId, l.ItemId, l.Quantity);
                }
            }
        }

        foreach (var d in await db.StockDocuments.AsNoTracking().Include(d => d.Lines).Where(d => d.OrganizationId == org).ToListAsync(ct))
        {
            var name = ($"{StockDocument.KindName(d.Kind)} {d.Number}", $"stock-documents/{d.Id}");
            documents[(StockSource.StockDocument, d.Id)] = name;
            documents[(StockSource.StockDocumentReversal, d.Id)] = ($"Сторно {name.Item1}", name.Item2);
            if (d.Status is StockDocumentStatus.Posted or StockDocumentStatus.Reversed)
            {
                foreach (var (warehouse, item, qty) in d.MovementDeltas())
                {
                    Expect(StockSource.StockDocument, d.Id, warehouse, item, qty);
                    if (d.Status == StockDocumentStatus.Reversed)
                    {
                        Expect(StockSource.StockDocumentReversal, d.Id, warehouse, item, -qty);
                    }
                }
            }
        }

        foreach (var d in await db.InventoryCounts.AsNoTracking().Include(d => d.Lines).Where(d => d.OrganizationId == org).ToListAsync(ct))
        {
            documents[(StockSource.Inventory, d.Id)] = ($"Инвентаризация {d.Number}", $"inventory/{d.Id}");
            if (d.Status == InventoryStatus.Posted)
            {
                foreach (var l in d.Lines)
                {
                    Expect(StockSource.Inventory, d.Id, d.WarehouseId, l.ItemId, (l.CountedQuantity ?? l.BookQuantity) - l.BookQuantity);
                }
            }
        }

        var itemCodes = await db.Items.AsNoTracking().Where(i => i.OrganizationId == org).ToDictionaryAsync(i => i.Id, i => i.Code, ct);
        var warehouseNames = await db.Warehouses.AsNoTracking().Where(w => w.OrganizationId == org).ToDictionaryAsync(w => w.Id, w => w.Name, ct);
        string Where(long warehouse, long item) =>
            $"{itemCodes.GetValueOrDefault(item, $"позиция №{item}")} на складе «{warehouseNames.GetValueOrDefault(warehouse, $"№{warehouse}")}»";

        var problems = new List<ReconciliationProblem>();
        void Add(ReconciliationProblem p)
        {
            if (problems.Count < MaxProblems)
            {
                problems.Add(p);
            }
        }

        foreach (var key in expected.Keys.Union(actual.Keys).OrderBy(k => k.Item1).ThenBy(k => k.Item2))
        {
            var want = expected.GetValueOrDefault(key);
            var have = actual.GetValueOrDefault(key);
            if (want == have)
            {
                continue;
            }

            var (source, id, warehouse, item) = key;
            if (!documents.TryGetValue((source, id), out var doc))
            {
                Add(new ReconciliationProblem($"Документ №{id}", null,
                    $"Движение {Where(warehouse, item)} ({Quantities.Format(have)}) без документа-основания."));
                continue;
            }

            Add(new ReconciliationProblem(doc.Name, doc.Link, want == 0
                ? $"{Where(warehouse, item)}: в регистре {Quantities.Format(have)}, а по документу движения быть не должно."
                : $"{Where(warehouse, item)}: в регистре {Quantities.Format(have)}, по документу {Quantities.Format(want)}."));
        }

        foreach (var negative in movements.GroupBy(m => (m.WarehouseId, m.ItemId)).Select(g => (g.Key, Sum: g.Sum(m => m.Quantity))).Where(x => x.Sum < 0))
        {
            Add(new ReconciliationProblem("Остатки", "stock",
                $"Отрицательный остаток: {Where(negative.Key.WarehouseId, negative.Key.ItemId)} = {Quantities.Format(negative.Sum)}."));
        }

        return new ReconciliationResult(documents.Count(d => d.Key.Item1 != StockSource.StockDocumentReversal), movements.Count,
            expected.Count, problems);
    }
}
