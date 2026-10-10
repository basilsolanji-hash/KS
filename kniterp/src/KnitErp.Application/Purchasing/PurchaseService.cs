using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Application.Structure;
using KnitErp.Application.Warehousing;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Purchasing;
using KnitErp.Domain.Warehousing;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Purchasing;

/// <summary>Поступило по заказу: ничего, частично, всё.</summary>
public enum ReceiptState : byte
{
    None = 0,
    Partial = 1,
    Full = 2,
}

public sealed record PurchaseOrderFilter(PurchaseOrderStatus? Status = null, string? Search = null, DateOnly? From = null, DateOnly? To = null);

/// <summary>Total — null, если у пользователя нет права «Цены и суммы».</summary>
public sealed record PurchaseOrderRowDto(
    long Id, string Number, DateOnly OrderDate, string Supplier, string Warehouse, DateOnly? ExpectedDate, PurchaseOrderStatus Status,
    ReceiptState Received, decimal? Total)
{
    public string StatusName => PurchaseOrder.StatusName(Status);
}

public sealed record PurchaseOrderLineDto(
    long ItemId, string Code, string Name, string UnitSymbol, byte Precision, decimal Quantity, decimal? Price, decimal? VatPercent,
    decimal? Amount, decimal? VatAmount, decimal Received, decimal Returned)
{
    public decimal Left => Math.Max(0, Quantity - Received + Returned);
}

public sealed record LinkedDocumentDto(long Id, string Number, StockOperationKind Kind, DateOnly Date, StockDocumentStatus Status)
{
    public string KindName => StockDocument.KindName(Kind);
    public string StatusName => StockDocument.StatusName(Status);
}

public sealed record SupplierPaymentDto(
    long Id, string Number, DateOnly Date, long SupplierId, string Supplier, long? OrderId, string? OrderNumber, decimal Amount, string? Comment,
    SupplierPaymentStatus Status, string? CancelReason, string CreatedBy, byte[] RowVersion);

/// <summary>Счёт поставщика (D68) — номер и дата из шапки заказа; сумма — по заказу, оплачено — оплаты по заказу.</summary>
public sealed record SupplierInvoiceRowDto(
    long OrderId, string OrderNumber, DateOnly OrderDate, string Invoice, long SupplierId, string Supplier, DateOnly? ExpectedDate,
    PurchaseOrderStatus Status, decimal Total, decimal Paid)
{
    public string StatusName => PurchaseOrder.StatusName(Status);
    public decimal ToPay => Math.Max(0, Total - Paid);
}

/// <summary>Суммы по заказу: поступило и возвращено — по ценам заказа с НДС; Debt — к оплате по заказу.</summary>
public sealed record PurchaseOrderDto(
    long Id, string Number, DateOnly OrderDate, long SupplierId, string Supplier, long WarehouseId, string Warehouse, DateOnly? ExpectedDate,
    string? SupplierInvoice, bool PricesIncludeVat, PurchaseOrderStatus Status, string? Comment, string CreatedBy, DateTime CreatedAtUtc,
    string? ConfirmedBy, DateTime? ConfirmedAtUtc, IReadOnlyList<PurchaseOrderLineDto> Lines, IReadOnlyList<LinkedDocumentDto> Documents,
    IReadOnlyList<SupplierPaymentDto> Payments, decimal? Total, decimal? VatTotal, decimal? ReceivedValue, decimal? ReturnedValue, decimal? Paid,
    bool CanEdit, bool CanSeePrices, bool CanCreateDocuments, byte[] RowVersion)
{
    public string StatusName => PurchaseOrder.StatusName(Status);
    public decimal? Debt => ReceivedValue is { } r && ReturnedValue is { } ret && Paid is { } p ? r - ret - p : null;
    public ReceiptState Received => PurchaseService.StateOf(Lines);
}

public sealed record PurchaseItemOptionDto(long Id, string Code, string Name, string UnitSymbol, byte Precision, decimal? VatPercent);

public sealed record VatOptionDto(string Name, decimal? Percent);

public sealed record PurchaseOptionsDto(
    IReadOnlyList<LookupDto> Suppliers, IReadOnlyList<LookupWarehouseDto> Warehouses, IReadOnlyList<PurchaseItemOptionDto> Items,
    IReadOnlyList<VatOptionDto> VatRates, string CurrencyCode);

/// <summary>Расчёты с поставщиком: поступило, возвращено, оплачено и долг (плюс — мы должны, минус — аванс поставщику).</summary>
public sealed record SupplierBalanceDto(long SupplierId, string Supplier, decimal Received, decimal Returned, decimal Paid)
{
    public decimal Debt => Received - Returned - Paid;
}

/// <summary>
/// Закупки (D64): заказы поставщикам с ценами и НДС, поступления и возвраты по заказу, оплаты и расчёты.
/// Склад — факты (количества), заказ — условия (цены): кладовщик оформляет поступление по заказу, не видя цен,
/// а сумма поступления считается по ценам заказа. Права: «Закупки: просмотр», «Закупки: заказы и оплаты»;
/// цены и суммы видны только с правом «Цены и суммы».
/// </summary>
public sealed class PurchaseService(
    IKnitErpDbContext db, IAccessGuard guard, ICurrentUser currentUser, IClock clock, StockDocumentService documents)
{
    public const int MaxRows = 2000;

    /// <summary>
    /// Счета поставщиков: заказы с указанным счётом поставщика, кроме черновиков и отменённых; сколько оплачено и осталось.
    /// Отдельного документа «счёт поставщика» нет (D64) — счёт записывается в шапку заказа.
    /// </summary>
    public async Task<IReadOnlyList<SupplierInvoiceRowDto>> ListSupplierInvoicesAsync(
        string? search = null, bool unpaidOnly = false, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.PurchaseView, ct);
        await guard.DemandAsync(Permissions.PriceView, ct);
        var rows = from o in db.PurchaseOrders.AsNoTracking()
                   where o.OrganizationId == ctx.OrganizationId && o.SupplierInvoice != null
                         && o.Status != PurchaseOrderStatus.Draft && o.Status != PurchaseOrderStatus.Cancelled
                   join c in db.Counterparties.AsNoTracking() on o.SupplierId equals c.Id
                   select new { o, Supplier = c.Name, c.Inn };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim();
            rows = rows.Where(r => r.o.Number.Contains(text) || r.o.SupplierInvoice!.Contains(text) || r.Supplier.Contains(text) || r.Inn == text);
        }

        var list = await rows.OrderByDescending(r => r.o.OrderDate).ThenByDescending(r => r.o.Id).Take(MaxRows)
            .Select(r => new { r.o.Id, r.o.Number, r.o.OrderDate, r.o.SupplierInvoice, r.o.SupplierId, r.Supplier, r.o.ExpectedDate, r.o.Status,
                Total = r.o.Lines.Sum(l => l.Amount) })
            .ToListAsync(ct);
        var ids = list.Select(r => r.Id).ToList();
        var paid = await db.SupplierPayments.AsNoTracking()
            .Where(p => p.OrganizationId == ctx.OrganizationId && p.PurchaseOrderId != null && ids.Contains(p.PurchaseOrderId.Value)
                        && p.Status == SupplierPaymentStatus.Posted)
            .GroupBy(p => p.PurchaseOrderId!.Value).Select(g => new { g.Key, Sum = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        return list.Select(r => new SupplierInvoiceRowDto(r.Id, r.Number, r.OrderDate, r.SupplierInvoice!, r.SupplierId, r.Supplier, r.ExpectedDate,
                r.Status, r.Total, paid.GetValueOrDefault(r.Id)))
            .Where(r => !unpaidOnly || r.ToPay > 0)
            .ToList();
    }

    public async Task<IReadOnlyList<PurchaseOrderRowDto>> ListOrdersAsync(PurchaseOrderFilter filter, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.PurchaseView, ct);
        var q = db.PurchaseOrders.AsNoTracking().Where(o => o.OrganizationId == ctx.OrganizationId);
        if (filter.Status is { } status)
        {
            q = q.Where(o => o.Status == status);
        }

        if (filter.From is { } from)
        {
            q = q.Where(o => o.OrderDate >= from);
        }

        if (filter.To is { } to)
        {
            q = q.Where(o => o.OrderDate <= to);
        }

        var rows = from o in q
                   join s in db.Counterparties.AsNoTracking() on o.SupplierId equals s.Id
                   join w in db.Warehouses.AsNoTracking() on o.WarehouseId equals w.Id
                   select new { o, Supplier = s.Name, s.Inn, Warehouse = w.Name };
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var text = filter.Search.Trim();
            rows = rows.Where(r => r.o.Number.Contains(text) || r.Supplier.Contains(text) || r.Inn == text
                                   || (r.o.SupplierInvoice != null && r.o.SupplierInvoice.Contains(text)));
        }

        var list = await rows.OrderByDescending(r => r.o.OrderDate).ThenByDescending(r => r.o.Id).Take(MaxRows)
            .Select(r => new { r.o.Id, r.o.Number, r.o.OrderDate, r.Supplier, r.Warehouse, r.o.ExpectedDate, r.o.Status,
                Lines = r.o.Lines.Select(l => new { l.ItemId, l.Quantity, l.Amount }).ToList() })
            .ToListAsync(ct);
        var ids = list.Select(o => o.Id).ToList();
        var moved = await MovedAsync(ctx, ids, ct);
        var prices = ctx.Permissions.Has(Permissions.PriceView);
        return list.Select(o => new PurchaseOrderRowDto(o.Id, o.Number, o.OrderDate, o.Supplier, o.Warehouse, o.ExpectedDate, o.Status,
                StateOf(o.Lines.Select(l => (l.Quantity, Net(moved, o.Id, l.ItemId)))),
                prices ? o.Lines.Sum(l => l.Amount) : null))
            .ToList();
    }

    public async Task<PurchaseOrderDto> GetOrderAsync(long id, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.PurchaseView, ct);
        var order = await db.PurchaseOrders.AsNoTracking().Include(o => o.Lines)
                        .SingleOrDefaultAsync(o => o.Id == id && o.OrganizationId == ctx.OrganizationId, ct)
                    ?? throw new NotFoundException("Заказ поставщику");
        var itemIds = order.Lines.Select(l => l.ItemId).ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id))
            .Join(db.Units.AsNoTracking(), i => i.UnitId, u => u.Id, (i, u) => new { i.Id, i.Code, i.Name, u.Symbol, u.Precision })
            .ToDictionaryAsync(x => x.Id, ct);
        var supplier = await db.Counterparties.AsNoTracking().Where(c => c.Id == order.SupplierId).Select(c => c.Name).SingleAsync(ct);
        var warehouse = await db.Warehouses.AsNoTracking().Where(w => w.Id == order.WarehouseId).Select(w => w.Name).SingleAsync(ct);
        long?[] userIds = [order.CreatedByUserId, order.ConfirmedByUserId];
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var docs = await db.StockDocuments.AsNoTracking()
            .Where(d => d.OrganizationId == ctx.OrganizationId && d.PurchaseOrderId == id)
            .OrderBy(d => d.DocumentDate).ThenBy(d => d.Id)
            .Select(d => new { d.Id, d.Number, d.Kind, d.DocumentDate, d.Status, Lines = d.Lines.Select(l => new { l.ItemId, l.Quantity }).ToList() })
            .ToListAsync(ct);
        decimal Moved(long itemId, StockOperationKind kind) =>
            docs.Where(d => d.Kind == kind && d.Status == StockDocumentStatus.Posted).SelectMany(d => d.Lines).Where(l => l.ItemId == itemId).Sum(l => l.Quantity);

        var prices = ctx.Permissions.Has(Permissions.PriceView);
        var lines = order.Lines.Select(l =>
        {
            var item = items[l.ItemId];
            return new PurchaseOrderLineDto(l.ItemId, item.Code, item.Name, item.Symbol, item.Precision, l.Quantity,
                prices ? l.Price : null, l.VatPercent, prices ? l.Amount : null, prices ? l.VatAmount : null,
                Moved(l.ItemId, StockOperationKind.Receipt), Moved(l.ItemId, StockOperationKind.ReturnToSupplier));
        }).OrderBy(l => l.Code).ToList();

        var payments = await PaymentsQuery(ctx, orderId: id).ToListAsync(ct);
        decimal Value(StockOperationKind kind) => Money.Round(docs.Where(d => d.Kind == kind && d.Status == StockDocumentStatus.Posted)
            .SelectMany(d => d.Lines).Sum(l => l.Quantity * order.UnitCostWithVat(l.ItemId)));

        var canEdit = ctx.Permissions.Has(Permissions.PurchaseEdit) && prices;
        var canDocs = order.Status is PurchaseOrderStatus.Confirmed or PurchaseOrderStatus.Closed
                      && WarehouseScope.Covers(ctx, Permissions.WarehouseDocumentCreate, order.WarehouseId);
        return new PurchaseOrderDto(order.Id, order.Number, order.OrderDate, order.SupplierId, supplier, order.WarehouseId, warehouse,
            order.ExpectedDate, order.SupplierInvoice, order.PricesIncludeVat, order.Status, order.Comment,
            users[order.CreatedByUserId], order.CreatedAtUtc, order.ConfirmedByUserId is { } c ? users[c] : null, order.ConfirmedAtUtc,
            lines, docs.Select(d => new LinkedDocumentDto(d.Id, d.Number, d.Kind, d.DocumentDate, d.Status)).ToList(),
            prices ? payments : [],
            prices ? order.Total : null, prices ? order.VatTotal : null,
            prices ? Value(StockOperationKind.Receipt) : null, prices ? Value(StockOperationKind.ReturnToSupplier) : null,
            prices ? payments.Where(p => p.Status == SupplierPaymentStatus.Posted).Sum(p => p.Amount) : null,
            canEdit, prices, canDocs, order.RowVersion);
    }

    /// <summary>Справочники формы заказа: поставщики, склады, позиции с НДС по умолчанию, ставки НДС организации.</summary>
    public async Task<PurchaseOptionsDto> GetOptionsAsync(DateOnly? onDate = null, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.PurchaseEdit, ct);
        var org = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == ctx.OrganizationId, ct);
        var date = onDate ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(clock.UtcNow, org.TimeZoneId));
        var suppliers = await db.Counterparties.AsNoTracking()
            .Where(c => c.OrganizationId == ctx.OrganizationId && !c.IsArchived && c.IsSupplier)
            .OrderBy(c => c.Name).Select(c => new LookupDto(c.Id, c.Name)).ToListAsync(ct);
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => w.OrganizationId == ctx.OrganizationId && !w.IsArchived)
            .OrderBy(w => w.Name).Select(w => new LookupWarehouseDto(w.Id, w.Name)).ToListAsync(ct);
        var rates = await db.VatRates.AsNoTracking().Include(r => r.Periods)
            .Where(r => r.OrganizationId == ctx.OrganizationId && !r.IsArchived).ToListAsync(ct);
        var items = await db.Items.AsNoTracking().Where(i => i.OrganizationId == ctx.OrganizationId && !i.IsArchived).OrderBy(i => i.Code)
            .Join(db.Units.AsNoTracking(), i => i.UnitId, u => u.Id, (i, u) => new { i.Id, i.Code, i.Name, u.Symbol, u.Precision, i.VatRateId })
            .ToListAsync(ct);
        var standard = rates.FirstOrDefault(r => r.Kind == VatRateKind.Standard)?.PercentOn(date);
        return new PurchaseOptionsDto(suppliers, warehouses,
            items.Select(i => new PurchaseItemOptionDto(i.Id, i.Code, i.Name, i.Symbol, i.Precision,
                i.VatRateId is { } rid && rates.FirstOrDefault(r => r.Id == rid) is { } rate ? rate.PercentOn(date) : standard)).ToList(),
            rates.OrderBy(r => r.Kind).ThenBy(r => r.Name).Select(r => new VatOptionDto(r.Name, r.PercentOn(date)))
                .Where(r => r.Percent is not null || rates.Any(x => x.Kind == VatRateKind.Exempt && x.Name == r.Name))
                .DistinctBy(r => r.Percent).ToList(),
            org.CurrencyCode);
    }

    public async Task<long> CreateOrderAsync(PurchaseOrderHeader header, CancellationToken ct = default)
    {
        var ctx = await DemandEditAsync(ct);
        await ValidateHeaderAsync(ctx, header, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var number = await DocumentNumbers.NextAsync(db, ctx.OrganizationId, PurchaseOrder.NumberPrefix, ct);
        var order = PurchaseOrder.Create(ctx.OrganizationId, number, header, ctx.UserId, clock.UtcNow);
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync(ct);
        Audit(ctx, AuditActions.PurchaseOrderCreated, nameof(PurchaseOrder), order.Id, null, $"{order.Number} от {order.OrderDate:dd.MM.yyyy}", null);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return order.Id;
    }

    public async Task UpdateOrderHeaderAsync(long id, PurchaseOrderHeader header, byte[] rowVersion, CancellationToken ct = default)
    {
        var (ctx, order) = await LoadForEditAsync(id, rowVersion, ct);
        await ValidateHeaderAsync(ctx, header, ct);
        order.UpdateHeader(header);
        Audit(ctx, AuditActions.PurchaseOrderChanged, nameof(PurchaseOrder), id, null, $"{order.OrderDate:dd.MM.yyyy}, счёт {order.SupplierInvoice ?? "—"}",
            $"{order.Number}: шапка");
        await db.SaveOrConflictAsync(ct);
    }

    public async Task SetOrderLineAsync(long id, long itemId, decimal quantity, decimal price, decimal? vatPercent, byte[] rowVersion,
        CancellationToken ct = default)
    {
        var (ctx, order) = await LoadForEditAsync(id, rowVersion, ct);
        var item = await StockEntry.ActiveItems(db, ctx.OrganizationId, itemId).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Номенклатура");
        StockEntry.EnsurePrecision(item, quantity);
        order.SetLine(itemId, quantity, price, vatPercent);
        var line = order.Lines.Single(l => l.ItemId == itemId);
        Audit(ctx, AuditActions.PurchaseOrderChanged, nameof(PurchaseOrder), id, null,
            $"{item.Code}: {Quantities.Format(quantity)} {item.UnitSymbol} × {price:0.####} = {line.Amount:0.00}", $"{order.Number}: строка");
        await db.SaveOrConflictAsync(ct);
    }

    public async Task RemoveOrderLineAsync(long id, long itemId, byte[] rowVersion, CancellationToken ct = default)
    {
        var (ctx, order) = await LoadForEditAsync(id, rowVersion, ct);
        order.RemoveLine(itemId);
        Audit(ctx, AuditActions.PurchaseOrderChanged, nameof(PurchaseOrder), id, $"позиция №{itemId}", "строка удалена", order.Number);
        await db.SaveOrConflictAsync(ct);
    }

    public async Task ConfirmOrderAsync(long id, byte[] rowVersion, CancellationToken ct = default)
    {
        var (ctx, order) = await LoadForEditAsync(id, rowVersion, ct);
        await ValidateHeaderAsync(ctx, Header(order), ct);
        order.Confirm(ctx.UserId, clock.UtcNow);
        Audit(ctx, AuditActions.PurchaseOrderConfirmed, nameof(PurchaseOrder), id, "Черновик", "Подтверждён", $"{order.Number}: {order.Total:0.00}");
        await db.SaveOrConflictAsync(ct);
    }

    public async Task CloseOrderAsync(long id, byte[] rowVersion, CancellationToken ct = default)
    {
        var (ctx, order) = await LoadForEditAsync(id, rowVersion, ct);
        order.Close();
        Audit(ctx, AuditActions.PurchaseOrderChanged, nameof(PurchaseOrder), id, "Подтверждён", "Закрыт", order.Number);
        await db.SaveOrConflictAsync(ct);
    }

    public async Task ReopenOrderAsync(long id, byte[] rowVersion, CancellationToken ct = default)
    {
        var (ctx, order) = await LoadForEditAsync(id, rowVersion, ct);
        order.Reopen();
        Audit(ctx, AuditActions.PurchaseOrderChanged, nameof(PurchaseOrder), id, "Закрыт", "Подтверждён", order.Number);
        await db.SaveOrConflictAsync(ct);
    }

    /// <summary>Отмена заказа. Если по нему уже есть документы склада или оплаты — нельзя: сначала их сторно и отмена.</summary>
    public async Task CancelOrderAsync(long id, byte[] rowVersion, CancellationToken ct = default)
    {
        var (ctx, order) = await LoadForEditAsync(id, rowVersion, ct);
        var hasDocs = await db.StockDocuments.AnyAsync(d => d.PurchaseOrderId == id
            && (d.Status == StockDocumentStatus.Posted || d.Status == StockDocumentStatus.Draft), ct);
        var hasPayments = await db.SupplierPayments.AnyAsync(p => p.PurchaseOrderId == id && p.Status == SupplierPaymentStatus.Posted, ct);
        if (hasDocs || hasPayments)
        {
            throw new BusinessRuleException("purchase.order.in_use",
                "По заказу есть поступления, возвраты или оплаты. Отмените черновики, сторнируйте проведённые документы и отмените оплаты — затем заказ.");
        }

        var before = PurchaseOrder.StatusName(order.Status);
        order.Cancel();
        Audit(ctx, AuditActions.PurchaseOrderCancelled, nameof(PurchaseOrder), id, before, "Отменён", order.Number);
        await db.SaveOrConflictAsync(ct);
    }

    /// <summary>Поступление по заказу: черновик со всем, что ещё не поступило. Кладовщик правит количества по факту и проводит.</summary>
    public async Task<long> CreateReceiptAsync(long orderId, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.PurchaseView, ct);
        var order = await LoadOrderAsync(ctx, orderId, ct);
        var moved = await MovedAsync(ctx, [orderId], ct);
        var lines = order.Lines.Select(l => (l.ItemId, Quantity: l.Quantity - Net(moved, orderId, l.ItemId))).Where(l => l.Quantity > 0).ToList();
        if (lines.Count == 0)
        {
            throw new BusinessRuleException("purchase.order.received", "По заказу всё уже поступило.");
        }

        var reason = await DefaultReasonAsync(ctx, StockOperationKind.Receipt, "Закупка у поставщика", ct);
        return await documents.CreateWithLinesAsync(StockOperationKind.Receipt,
            new StockDocumentHeader(order.WarehouseId, null, order.SupplierId, reason, await TodayAsync(ctx, ct), $"По заказу {order.Number}", orderId),
            lines, ct);
    }

    /// <summary>Возврат по заказу: черновик со всем полученным; лишние строки удаляют, количества уменьшают.</summary>
    public async Task<long> CreateReturnAsync(long orderId, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.PurchaseView, ct);
        var order = await LoadOrderAsync(ctx, orderId, ct);
        var moved = await MovedAsync(ctx, [orderId], ct);
        var lines = order.Lines.Select(l => (l.ItemId, Quantity: Net(moved, orderId, l.ItemId))).Where(l => l.Quantity > 0).ToList();
        if (lines.Count == 0)
        {
            throw new BusinessRuleException("purchase.order.nothing_received", "По заказу ещё ничего не поступило — возвращать нечего.");
        }

        var reason = await DefaultReasonAsync(ctx, StockOperationKind.ReturnToSupplier, "Возврат поставщику", ct);
        return await documents.CreateWithLinesAsync(StockOperationKind.ReturnToSupplier,
            new StockDocumentHeader(order.WarehouseId, null, order.SupplierId, reason, await TodayAsync(ctx, ct), $"Возврат по заказу {order.Number}", orderId),
            lines, ct);
    }

    public async Task<IReadOnlyList<SupplierPaymentDto>> ListPaymentsAsync(DateOnly? from = null, DateOnly? to = null, string? search = null,
        CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.PurchaseView, ct);
        await guard.DemandAsync(Permissions.PriceView, ct);
        return await PaymentsQuery(ctx, from: from, to: to, search: search).ToListAsync(ct);
    }

    /// <summary>Оплата поставщику: сразу уменьшает долг. Заказ — по желанию, того же поставщика.</summary>
    public async Task<long> CreatePaymentAsync(DateOnly date, long supplierId, long? orderId, decimal amount, string? comment, CancellationToken ct = default)
    {
        var ctx = await DemandEditAsync(ct);
        var supplier = await db.Counterparties.AsNoTracking().SingleOrDefaultAsync(c => c.Id == supplierId && c.OrganizationId == ctx.OrganizationId, ct)
                       ?? throw new NotFoundException("Контрагент");
        if (!supplier.IsSupplier)
        {
            throw new BusinessRuleException("stock.document.not_supplier", $"«{supplier.Name}» не отмечен как поставщик.");
        }

        if (orderId is { } oid)
        {
            var order = await db.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == oid && o.OrganizationId == ctx.OrganizationId, ct)
                        ?? throw new NotFoundException("Заказ поставщику");
            if (order.SupplierId != supplierId || order.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Cancelled)
            {
                throw new BusinessRuleException("purchase.payment.order", $"Заказ {order.Number} другого поставщика или не подтверждён.");
            }
        }

        await ClosedPeriod.EnsureOpenAsync(db, ctx.OrganizationId, date, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var number = await DocumentNumbers.NextAsync(db, ctx.OrganizationId, SupplierPayment.NumberPrefix, ct);
        var payment = SupplierPayment.Create(ctx.OrganizationId, number, date, supplierId, orderId, amount, comment, ctx.UserId, clock.UtcNow);
        db.SupplierPayments.Add(payment);
        await db.SaveChangesAsync(ct);
        Audit(ctx, AuditActions.SupplierPaymentCreated, nameof(SupplierPayment), payment.Id, null, $"{payment.Amount:0.00}",
            $"{number}: {supplier.Name}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return payment.Id;
    }

    public async Task CancelPaymentAsync(long id, string? reason, byte[] rowVersion, CancellationToken ct = default)
    {
        var ctx = await DemandEditAsync(ct);
        var payment = await db.SupplierPayments.SingleOrDefaultAsync(p => p.Id == id && p.OrganizationId == ctx.OrganizationId, ct)
                      ?? throw new NotFoundException("Оплата");
        payment.EnsureVersion(payment.RowVersion, rowVersion);
        await ClosedPeriod.EnsureOpenAsync(db, ctx.OrganizationId, payment.PaymentDate, ct);
        payment.Cancel(ctx.UserId, reason, clock.UtcNow);
        Audit(ctx, AuditActions.SupplierPaymentCancelled, nameof(SupplierPayment), id, $"{payment.Amount:0.00}", "отменена",
            $"{payment.Number}: {payment.CancelReason}");
        await db.SaveOrConflictAsync(ct);
    }

    /// <summary>
    /// Расчёты с поставщиками на дату: поступило и возвращено по проведённым документам по заказам (по ценам заказа с НДС),
    /// оплачено — действующие оплаты. Поступления без заказа в расчёты не входят: у них нет цены.
    /// </summary>
    public async Task<IReadOnlyList<SupplierBalanceDto>> BalancesAsync(DateOnly? asOf = null, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.PurchaseView, ct);
        await guard.DemandAsync(Permissions.PriceView, ct);
        var org = ctx.OrganizationId;
        var docs = await db.StockDocuments.AsNoTracking()
            .Where(d => d.OrganizationId == org && d.PurchaseOrderId != null && d.Status == StockDocumentStatus.Posted
                        && (asOf == null || d.DocumentDate <= asOf))
            .SelectMany(d => d.Lines.Select(l => new { d.Kind, OrderId = d.PurchaseOrderId!.Value, l.ItemId, l.Quantity }))
            .ToListAsync(ct);
        var orderIds = docs.Select(d => d.OrderId).Distinct().ToList();
        var orders = await db.PurchaseOrders.AsNoTracking().Include(o => o.Lines).Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var paid = await db.SupplierPayments.AsNoTracking()
            .Where(p => p.OrganizationId == org && p.Status == SupplierPaymentStatus.Posted && (asOf == null || p.PaymentDate <= asOf))
            .GroupBy(p => p.SupplierId).Select(g => new { g.Key, Sum = g.Sum(p => p.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);

        var moved = docs.GroupBy(d => orders[d.OrderId].SupplierId).ToDictionary(g => g.Key, g => (
            Received: Money.Round(g.Where(d => d.Kind == StockOperationKind.Receipt).Sum(d => d.Quantity * orders[d.OrderId].UnitCostWithVat(d.ItemId))),
            Returned: Money.Round(g.Where(d => d.Kind == StockOperationKind.ReturnToSupplier).Sum(d => d.Quantity * orders[d.OrderId].UnitCostWithVat(d.ItemId)))));
        var supplierIds = moved.Keys.Union(paid.Keys).ToList();
        var names = await db.Counterparties.AsNoTracking().Where(c => supplierIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return supplierIds.Select(id => new SupplierBalanceDto(id, names[id],
                moved.TryGetValue(id, out var m) ? m.Received : 0, moved.TryGetValue(id, out var r) ? r.Returned : 0, paid.GetValueOrDefault(id)))
            .OrderByDescending(b => b.Debt).ThenBy(b => b.Supplier).ToList();
    }

    public static ReceiptState StateOf(IEnumerable<PurchaseOrderLineDto> lines) =>
        StateOf(lines.Select(l => (l.Quantity, l.Received - l.Returned)));

    private static ReceiptState StateOf(IEnumerable<(decimal Ordered, decimal Net)> lines)
    {
        var list = lines.ToList();
        if (list.Count == 0 || list.All(l => l.Net <= 0))
        {
            return ReceiptState.None;
        }

        return list.All(l => l.Net >= l.Ordered) ? ReceiptState.Full : ReceiptState.Partial;
    }

    /// <summary>Поступило минус возвращено по заказу и позиции — по проведённым документам.</summary>
    private static decimal Net(IReadOnlyList<(long OrderId, StockOperationKind Kind, long ItemId, decimal Quantity)> moved, long orderId, long itemId) =>
        moved.Where(m => m.OrderId == orderId && m.ItemId == itemId)
            .Sum(m => m.Kind == StockOperationKind.Receipt ? m.Quantity : -m.Quantity);

    private async Task<IReadOnlyList<(long OrderId, StockOperationKind Kind, long ItemId, decimal Quantity)>> MovedAsync(
        AccessContext ctx, IReadOnlyCollection<long> orderIds, CancellationToken ct) =>
        (await db.StockDocuments.AsNoTracking()
            .Where(d => d.OrganizationId == ctx.OrganizationId && d.PurchaseOrderId != null && orderIds.Contains(d.PurchaseOrderId.Value)
                        && d.Status == StockDocumentStatus.Posted)
            .SelectMany(d => d.Lines.Select(l => new { OrderId = d.PurchaseOrderId!.Value, d.Kind, l.ItemId, l.Quantity }))
            .ToListAsync(ct))
        .Select(x => (x.OrderId, x.Kind, x.ItemId, x.Quantity)).ToList();

    /// <summary>Оплаты с фильтрами; фильтры — до проекции в DTO, чтобы запрос переводился в SQL.</summary>
    private IQueryable<SupplierPaymentDto> PaymentsQuery(
        AccessContext ctx, long? orderId = null, DateOnly? from = null, DateOnly? to = null, string? search = null)
    {
        var q = from p in db.SupplierPayments.AsNoTracking()
                where p.OrganizationId == ctx.OrganizationId
                join s in db.Counterparties.AsNoTracking() on p.SupplierId equals s.Id
                join u in db.Users.AsNoTracking() on p.CreatedByUserId equals u.Id
                join o in db.PurchaseOrders.AsNoTracking() on p.PurchaseOrderId equals o.Id into oj
                from o in oj.DefaultIfEmpty()
                select new { p, Supplier = s.Name, Author = u.DisplayName, OrderNumber = o == null ? null : o.Number };
        if (orderId is { } oid)
        {
            q = q.Where(x => x.p.PurchaseOrderId == oid);
        }

        if (from is { } f)
        {
            q = q.Where(x => x.p.PaymentDate >= f);
        }

        if (to is { } t)
        {
            q = q.Where(x => x.p.PaymentDate <= t);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim();
            q = q.Where(x => x.p.Number.Contains(text) || x.Supplier.Contains(text) || (x.OrderNumber != null && x.OrderNumber.Contains(text)));
        }

        return q.OrderByDescending(x => x.p.PaymentDate).ThenByDescending(x => x.p.Id).Take(MaxRows)
            .Select(x => new SupplierPaymentDto(x.p.Id, x.p.Number, x.p.PaymentDate, x.p.SupplierId, x.Supplier, x.p.PurchaseOrderId, x.OrderNumber,
                x.p.Amount, x.p.Comment, x.p.Status, x.p.CancelReason, x.Author, x.p.RowVersion));
    }

    private async Task<long?> DefaultReasonAsync(AccessContext ctx, StockOperationKind kind, string preferred, CancellationToken ct)
    {
        var reasons = await db.OperationReasons.AsNoTracking()
            .Where(r => r.OrganizationId == ctx.OrganizationId && r.Kind == kind && !r.IsArchived)
            .Select(r => new { r.Id, r.Name }).ToListAsync(ct);
        return (reasons.FirstOrDefault(r => r.Name == preferred) ?? reasons.FirstOrDefault())?.Id;
    }

    private async Task<DateOnly> TodayAsync(AccessContext ctx, CancellationToken ct)
    {
        var tz = await db.Organizations.AsNoTracking().Where(o => o.Id == ctx.OrganizationId).Select(o => o.TimeZoneId).SingleAsync(ct);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(clock.UtcNow, tz));
    }

    private async Task ValidateHeaderAsync(AccessContext ctx, PurchaseOrderHeader h, CancellationToken ct)
    {
        var supplier = await db.Counterparties.AsNoTracking().SingleOrDefaultAsync(c => c.Id == h.SupplierId && c.OrganizationId == ctx.OrganizationId, ct)
                       ?? throw new NotFoundException("Контрагент");
        if (supplier.IsArchived)
        {
            throw new BusinessRuleException("catalog.archived", $"Контрагент «{supplier.Name}» в архиве.");
        }

        if (!supplier.IsSupplier)
        {
            throw new BusinessRuleException("stock.document.not_supplier", $"«{supplier.Name}» не отмечен как поставщик.");
        }

        var warehouse = await db.Warehouses.AsNoTracking().SingleOrDefaultAsync(w => w.Id == h.WarehouseId && w.OrganizationId == ctx.OrganizationId, ct)
                        ?? throw new NotFoundException("Склад");
        if (warehouse.IsArchived)
        {
            throw new BusinessRuleException("catalog.archived", $"Склад «{warehouse.Name}» в архиве.");
        }
    }

    private static PurchaseOrderHeader Header(PurchaseOrder o) =>
        new(o.OrderDate, o.SupplierId, o.WarehouseId, o.ExpectedDate, o.SupplierInvoice, o.PricesIncludeVat, o.Comment);

    /// <summary>Правка заказа и оплат — право «Закупки: заказы и оплаты» вместе с «Цены и суммы»: без цен заказ не составить.</summary>
    private async Task<AccessContext> DemandEditAsync(CancellationToken ct)
    {
        var ctx = await guard.DemandAsync(Permissions.PurchaseEdit, ct);
        await guard.DemandAsync(Permissions.PriceView, ct);
        return ctx;
    }

    private async Task<PurchaseOrder> LoadOrderAsync(AccessContext ctx, long id, CancellationToken ct) =>
        await db.PurchaseOrders.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == id && o.OrganizationId == ctx.OrganizationId, ct)
        ?? throw new NotFoundException("Заказ поставщику");

    private async Task<(AccessContext, PurchaseOrder)> LoadForEditAsync(long id, byte[] rowVersion, CancellationToken ct)
    {
        var ctx = await DemandEditAsync(ct);
        var order = await LoadOrderAsync(ctx, id, ct);
        return (ctx, order.EnsureVersion(order.RowVersion, rowVersion));
    }

    private void Audit(AccessContext ctx, string action, string entity, long id, string? before, string? after, string? reason) =>
        db.AuditEntries.Add(AuditEntry.Create(clock.UtcNow, ctx.OrganizationId, ctx.UserId, action, entity, id.ToString(),
            before, after, reason, currentUser.CorrelationId));
}
