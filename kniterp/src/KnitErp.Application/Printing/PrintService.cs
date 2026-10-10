using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Common;
using KnitErp.Domain.Organizations;
using KnitErp.Domain.Sales;
using KnitErp.Domain.Warehousing;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Printing;

/// <summary>Сторона документа в печатной форме. Kpp — только сверенный (D15).</summary>
public sealed record PrintPartyDto(string Name, string? Inn, string? Kpp, string? Address);

/// <summary>Строка печатной формы: цена и стоимость без НДС, НДС, стоимость с НДС.</summary>
public sealed record PrintLineDto(
    int No, string Code, string Name, string UnitSymbol, string UnitCode, decimal Quantity, decimal Price, decimal? VatPercent,
    decimal AmountWithoutVat, decimal VatAmount, decimal Amount);

public sealed record InvoicePrintDto(
    string Number, DateOnly Date, DateOnly? DueDate, PrintPartyDto Seller, PrintRequisites Requisites, PrintPartyDto Buyer, string Basis,
    bool PricesIncludeVat, IReadOnlyList<PrintLineDto> Lines, decimal Total, decimal VatTotal, string TotalInWords, string CurrencyCode,
    bool Cancelled);

public sealed record UpdPrintDto(
    string Number, DateOnly Date, PrintPartyDto Seller, PrintRequisites Requisites, PrintPartyDto Buyer, string Basis, string Warehouse,
    IReadOnlyList<PrintLineDto> Lines, decimal TotalWithoutVat, decimal VatTotal, decimal Total, string TotalInWords, string Currency,
    bool Reversed);

/// <summary>
/// Данные печатных форм (D68): счёт на оплату — по счёту покупателю; УПД (статус 1: счёт-фактура и передаточный документ) —
/// по проведённой отгрузке по заказу, цены — из заказа. Формы печатаются по-русски: это документы для российского учёта.
/// Права — как у денег продаж: «Продажи: просмотр» и «Цены и суммы».
/// </summary>
public sealed class PrintService(IKnitErpDbContext db, IAccessGuard guard)
{
    private static readonly Dictionary<string, string> CurrencyNumeric = new() { ["RUB"] = "643", ["KZT"] = "398", ["UZS"] = "860", ["BYN"] = "933" };

    public async Task<InvoicePrintDto> InvoiceAsync(long invoiceId, CancellationToken ct = default)
    {
        var ctx = await DemandAsync(ct);
        var invoice = await db.CustomerInvoices.AsNoTracking().Include(i => i.Lines)
                          .SingleOrDefaultAsync(i => i.Id == invoiceId && i.OrganizationId == ctx.OrganizationId, ct)
                      ?? throw new NotFoundException("Счёт покупателю");
        var org = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == ctx.OrganizationId, ct);
        var order = await db.SalesOrders.AsNoTracking().SingleAsync(o => o.Id == invoice.SalesOrderId, ct);
        var items = await ItemsAsync(invoice.Lines.Select(l => l.ItemId), ct);
        var lines = invoice.Lines
            .Select(l => (Item: items[l.ItemId], l.Quantity, l.Price, l.VatPercent, l.Amount, l.VatAmount))
            .OrderBy(l => l.Item.Code)
            .Select((l, i) => new PrintLineDto(i + 1, l.Item.Code, l.Item.Name, l.Item.Symbol, l.Item.UnitCode, l.Quantity, l.Price, l.VatPercent,
                l.Amount - l.VatAmount, l.VatAmount, l.Amount))
            .ToList();
        return new InvoicePrintDto(invoice.Number, invoice.InvoiceDate, invoice.DueDate, Seller(org), Requisites(org),
            await BuyerAsync(invoice.CustomerId, ct), Basis(order), invoice.PricesIncludeVat, lines, invoice.Total, invoice.VatTotal,
            AmountInWords.Format(invoice.Total, org.CurrencyCode), org.CurrencyCode, invoice.Status == CustomerInvoiceStatus.Cancelled);
    }

    public async Task<UpdPrintDto> UpdAsync(long stockDocumentId, CancellationToken ct = default)
    {
        var ctx = await DemandAsync(ct);
        var doc = await db.StockDocuments.AsNoTracking().Include(d => d.Lines)
                      .SingleOrDefaultAsync(d => d.Id == stockDocumentId && d.OrganizationId == ctx.OrganizationId, ct)
                  ?? throw new NotFoundException("Документ");
        if (doc.Kind != StockOperationKind.Shipment || doc.SalesOrderId is null
            || doc.Status is not (StockDocumentStatus.Posted or StockDocumentStatus.Reversed))
        {
            throw new BusinessRuleException("print.upd.document", "УПД печатается по проведённой отгрузке по заказу покупателя.");
        }

        var org = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == ctx.OrganizationId, ct);
        var order = await db.SalesOrders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == doc.SalesOrderId, ct);
        var warehouse = await db.Warehouses.AsNoTracking().Where(w => w.Id == doc.WarehouseId).Select(w => w.Name).SingleAsync(ct);
        var items = await ItemsAsync(doc.Lines.Select(l => l.ItemId), ct);
        var lines = doc.Lines
            .Select(l =>
            {
                var orderLine = order.Lines.Single(o => o.ItemId == l.ItemId);
                var (amount, vat) = Money.LineAmounts(l.Quantity, orderLine.Price, orderLine.VatPercent, order.PricesIncludeVat);
                return (Item: items[l.ItemId], l.Quantity, orderLine.VatPercent, Amount: amount, Vat: vat);
            })
            .OrderBy(l => l.Item.Code)
            .Select((l, i) => new PrintLineDto(i + 1, l.Item.Code, l.Item.Name, l.Item.Symbol, l.Item.UnitCode, l.Quantity,
                Money.Round((l.Amount - l.Vat) / l.Quantity), l.VatPercent, l.Amount - l.Vat, l.Vat, l.Amount))
            .ToList();
        var total = lines.Sum(l => l.Amount);
        var country = Countries.Get(org.CountryCode);
        return new UpdPrintDto(doc.Number, doc.DocumentDate, Seller(org), Requisites(org), await BuyerAsync(doc.CounterpartyId!.Value, ct),
            Basis(order), warehouse, lines, lines.Sum(l => l.AmountWithoutVat), lines.Sum(l => l.VatAmount), total,
            AmountInWords.Format(total, org.CurrencyCode),
            $"{country.CurrencyName}, {CurrencyNumeric.GetValueOrDefault(org.CurrencyCode, org.CurrencyCode)}",
            doc.Status == StockDocumentStatus.Reversed);
    }

    private async Task<AccessContext> DemandAsync(CancellationToken ct)
    {
        var ctx = await guard.DemandAsync(Permissions.SalesView, ct);
        await guard.DemandAsync(Permissions.PriceView, ct);
        return ctx;
    }

    private static PrintPartyDto Seller(Organization org) => new(org.FullName, org.Inn, org.PrintableKpp, org.LegalAddress ?? org.ActualAddress);

    private static PrintRequisites Requisites(Organization o) =>
        new(o.LegalAddress ?? o.ActualAddress, o.BankName, o.BankBic, o.BankAccount, o.BankCorrAccount, o.DirectorName, o.AccountantName);

    private async Task<PrintPartyDto> BuyerAsync(long counterpartyId, CancellationToken ct) =>
        await db.Counterparties.AsNoTracking().Where(c => c.Id == counterpartyId)
            .Select(c => new PrintPartyDto(c.Name, c.Inn, c.Kpp, c.Address)).SingleAsync(ct);

    /// <summary>Основание: договор из заказа (если указан) и сам заказ.</summary>
    private static string Basis(SalesOrder order) =>
        (order.CustomerReference is { } reference ? reference + "; " : string.Empty) + $"заказ {order.Number} от {order.OrderDate:dd.MM.yyyy}";

    private sealed record PrintItem(string Code, string Name, string Symbol, string UnitCode);

    private async Task<Dictionary<long, PrintItem>> ItemsAsync(IEnumerable<long> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Items.AsNoTracking().Where(i => list.Contains(i.Id))
            .Join(db.Units.AsNoTracking(), i => i.UnitId, u => u.Id, (i, u) => new { i.Id, Item = new PrintItem(i.Code, i.Name, u.Symbol, u.Code) })
            .ToDictionaryAsync(x => x.Id, x => x.Item, ct);
    }
}
