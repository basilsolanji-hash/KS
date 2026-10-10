using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Application.Warehousing;
using KnitErp.Domain.Access;
using KnitErp.Domain.Production;
using KnitErp.Domain.Warehousing;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Organizations;

/// <summary>Показатель главного экрана: число, подпись и куда ведёт щелчок. Warn — требует внимания.</summary>
public sealed record DashboardTileDto(string Title, int Value, string Hint, string Href, bool Warn = false);

public sealed record DashboardDocumentDto(DateOnly Date, string Kind, string Number, string Warehouse, string Status, string Href);

/// <summary>Документы склада за месяц по видам — количество проведённых.</summary>
public sealed record DashboardMonthDto(int Receipts, int Transfers, int WriteOffs, int Inventories, DateOnly From, DateOnly To);

public sealed record DashboardDto(
    string OrganizationName, IReadOnlyList<DashboardTileDto> Tiles, DashboardMonthDto? Month, IReadOnlyList<DashboardDocumentDto> Recent,
    int? ReadinessPercent);

/// <summary>
/// Главный экран: показатели, что ждёт действия, последние документы. Каждый блок — только при праве на его данные
/// и в пределах складов пользователя; без прав на склад экран показывает то, что доступно (справочники, задачи).
/// </summary>
public sealed class DashboardService(IKnitErpDbContext db, IAccessGuard guard, IClock clock, LaunchReadinessService readiness)
{
    private static readonly string[] DocumentPermissions =
        [Permissions.WarehouseDocumentCreate, Permissions.WarehouseDocumentPost, Permissions.WarehouseReportView];

    public async Task<DashboardDto> GetAsync(CancellationToken ct = default)
    {
        var ctx = await guard.CurrentAsync(ct);
        var org = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == ctx.OrganizationId, ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(clock.UtcNow, org.TimeZoneId));
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var tiles = new List<DashboardTileDto>();
        DashboardMonthDto? month = null;
        var recent = new List<DashboardDocumentDto>();

        if (DocumentPermissions.Any(ctx.Permissions.Has))
        {
            var visible = WarehouseScope.Visible(ctx, DocumentPermissions);
            var docs = db.StockDocuments.AsNoTracking().Where(d => d.OrganizationId == ctx.OrganizationId
                && (visible == null || visible.Contains(d.WarehouseId) || (d.TargetWarehouseId != null && visible.Contains(d.TargetWarehouseId.Value))));
            var drafts = await docs.CountAsync(d => d.Status == StockDocumentStatus.Draft, ct);
            tiles.Add(new DashboardTileDto("Черновики документов", drafts, "ждут проведения", "stock-documents?status=1", drafts > 0));

            var counts = db.InventoryCounts.AsNoTracking()
                .Where(d => d.OrganizationId == ctx.OrganizationId && (visible == null || visible.Contains(d.WarehouseId)));
            var openCounts = await counts.CountAsync(d => d.Status == InventoryStatus.Draft, ct);
            tiles.Add(new DashboardTileDto("Инвентаризации в работе", openCounts, "не проведены", "inventory", openCounts > 0));

            var posted = await docs.Where(d => d.Status != StockDocumentStatus.Draft && d.Status != StockDocumentStatus.Cancelled
                                               && d.DocumentDate >= monthStart && d.DocumentDate <= today)
                .GroupBy(d => d.Kind).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var inventories = await counts.CountAsync(d => d.Status == InventoryStatus.Posted && d.CountDate >= monthStart && d.CountDate <= today, ct);
            month = new DashboardMonthDto(posted.GetValueOrDefault(StockOperationKind.Receipt), posted.GetValueOrDefault(StockOperationKind.Transfer),
                posted.GetValueOrDefault(StockOperationKind.WriteOff), inventories, monthStart, today);

            recent = (await (from d in docs.OrderByDescending(d => d.Id).Take(8)
                             join w in db.Warehouses.AsNoTracking() on d.WarehouseId equals w.Id
                             select new { d.Id, d.DocumentDate, d.Kind, d.Number, w.Name, d.Status }).ToListAsync(ct))
                .Select(d => new DashboardDocumentDto(d.DocumentDate, StockDocument.KindName(d.Kind), d.Number, d.Name,
                    StockDocument.StatusName(d.Status), $"stock-documents/{d.Id}"))
                .ToList();
        }

        if (ctx.Permissions.Has(Permissions.OpeningBalanceApprove))
        {
            var visible = WarehouseScope.Visible(ctx, Permissions.OpeningBalanceApprove);
            var waiting = await db.OpeningBalances.AsNoTracking().CountAsync(d => d.OrganizationId == ctx.OrganizationId
                && d.Status == OpeningBalanceStatus.Submitted && (visible == null || visible.Contains(d.WarehouseId)), ct);
            tiles.Add(new DashboardTileDto("Начальные остатки", waiting, "на утверждении", "opening-balances", waiting > 0));
        }

        if (ctx.Permissions.Has(Permissions.WarehouseReportView))
        {
            var visible = WarehouseScope.Visible(ctx, Permissions.WarehouseReportView);
            var positions = await db.StockMovements.AsNoTracking()
                .Where(m => m.OrganizationId == ctx.OrganizationId && (visible == null || visible.Contains(m.WarehouseId)))
                .GroupBy(m => new { m.WarehouseId, m.ItemId }).Where(g => g.Sum(m => m.Quantity) != 0).CountAsync(ct);
            tiles.Add(new DashboardTileDto("Позиций на остатке", positions, "склад × позиция", "stock"));
        }

        if (ctx.Permissions.Has(Permissions.CatalogView))
        {
            var items = await db.Items.AsNoTracking().CountAsync(i => i.OrganizationId == ctx.OrganizationId && !i.IsArchived, ct);
            tiles.Add(new DashboardTileDto("Номенклатура", items, "действующих позиций", "catalog"));
            var cards = await db.TechCards.AsNoTracking().CountAsync(c => c.OrganizationId == ctx.OrganizationId && c.Status == TechCardStatus.Active, ct);
            tiles.Add(new DashboardTileDto("Техкарты", cards, "действующих", "tech-cards"));
        }

        int? readinessPercent = null;
        if (ctx.Permissions.Has(Permissions.OrganizationEdit))
        {
            var report = await readiness.GetAsync(ct);
            var checkable = report.Items.Where(i => i.Done is not null).ToList();
            readinessPercent = checkable.Count == 0 ? 100 : checkable.Count(i => i.Done == true) * 100 / checkable.Count;
        }

        return new DashboardDto(org.ShortName, tiles, month, recent, readinessPercent);
    }
}
